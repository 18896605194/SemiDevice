(() => {
  'use strict';
  const root = document.getElementById('frontend');
  if (!root || !window.FRONTEND_CONTROLS) return;
  const assets = new URL('.', document.querySelector('script[src$="frontend-controls/guide.js"]').src);
  const catalog = window.FRONTEND_CONTROLS.controls;
  const data = Object.fromEntries(catalog.map(c => [c.name,c]));
  const loads = new Map();
  function loadMesh(file) {
    if (!loads.has(file)) loads.set(file, new Promise((resolve,reject) => {
      const script = document.createElement('script'); script.src = new URL(file,assets).href;
      script.onload = () => resolve(window.WPF_MESHES[file.replace('.mesh.js','')]);
      script.onerror = () => reject(new Error('未能读取本地模型，请保留 HTML 旁的 docs/frontend-controls 目录。'));
      document.head.append(script);
    }));
    return loads.get(file);
  }

  class MeshViewer {
    constructor(demo,control) {
      this.demo=demo; this.control=control; this.stage=demo.querySelector('.fe-demo-stage');
      this.index=0; this.native=false; this.visible=false; this.yaw=-0.48; this.pitch=0.34; this.zoom=0.84; this.version=0;
      this.base=null; this.meshes=[]; this.handles=[];
      demo.querySelector('.fe-reset').onclick=()=>{ this.yaw=-0.48; this.pitch=0.34; this.zoom=0.84; this.draw(); };
      demo.querySelector('.fe-native').onclick=()=>{ this.native=!this.native; demo.querySelector('.fe-native').setAttribute('aria-pressed',String(this.native)); this.sync(); };
      this.resize = new ResizeObserver(()=>this.draw()); this.resize.observe(this.stage);
    }
    async sync() {
      const version=++this.version;
      if(!this.visible || this.native || !this.control.variants[this.index]?.mesh) { this.demo.dataset.meshReady='inactive'; this.release(); return; }
      this.demo.dataset.meshReady='loading';
      try {
        const raw=await loadMesh(this.control.variants[this.index].mesh);
        if(version!==this.version || !this.visible || this.native) return;
        if(!this.gl) this.init();
        this.prepare(raw); this.draw(); this.demo.dataset.meshReady='true';
      } catch(error) { this.release(); this.demo.querySelector('output').textContent=error.message+' 已显示原生截图。'; this.demo.dataset.meshReady='fallback'; }
    }
    init() {
      this.canvas=document.createElement('canvas'); this.canvas.tabIndex=0;
      this.canvas.setAttribute('aria-label',this.control.name+' 三维模型，方向键旋转，滚轮或加减号缩放');
      const gl=this.canvas.getContext('webgl2',{alpha:false,antialias:true,preserveDrawingBuffer:true});
      if(!gl) throw new Error('当前浏览器不支持 WebGL2');
      this.gl=gl; this.stage.append(this.canvas);
      const compile=(type,code)=>{const s=gl.createShader(type);gl.shaderSource(s,code);gl.compileShader(s);if(!gl.getShaderParameter(s,gl.COMPILE_STATUS))throw new Error(gl.getShaderInfoLog(s));return s;};
      const vertex=compile(gl.VERTEX_SHADER,`#version 300 es
        in vec3 aPosition; in vec3 aNormal; uniform mat3 uRotation; uniform float uZoom; uniform float uAspect; out vec3 vNormal;
        void main(){vec3 p=uRotation*aPosition;vNormal=uRotation*aNormal;gl_Position=vec4(p.x*uZoom/uAspect,p.y*uZoom,-p.z*0.18,1.0);}`);
      const fragment=compile(gl.FRAGMENT_SHADER,`#version 300 es
        precision mediump float; in vec3 vNormal; uniform vec4 uColor; out vec4 frag;
        void main(){vec3 n=normalize(vNormal);if(!gl_FrontFacing)n=-n;float light=0.48+0.52*max(dot(n,normalize(vec3(-0.5,0.8,1.0))),0.0);frag=vec4(uColor.rgb*light,uColor.a);}`);
      this.program=gl.createProgram();gl.attachShader(this.program,vertex);gl.attachShader(this.program,fragment);gl.linkProgram(this.program);
      if(!gl.getProgramParameter(this.program,gl.LINK_STATUS))throw new Error(gl.getProgramInfoLog(this.program));
      gl.deleteShader(vertex);gl.deleteShader(fragment);
      this.pos=gl.getAttribLocation(this.program,'aPosition');this.normal=gl.getAttribLocation(this.program,'aNormal');
      this.rotation=gl.getUniformLocation(this.program,'uRotation');this.scale=gl.getUniformLocation(this.program,'uZoom');
      this.aspect=gl.getUniformLocation(this.program,'uAspect');this.color=gl.getUniformLocation(this.program,'uColor');
      let press=null;
      this.canvas.onpointerdown=e=>{press={x:e.clientX,y:e.clientY,yaw:this.yaw,pitch:this.pitch};this.canvas.setPointerCapture(e.pointerId);this.canvas.focus();};
      this.canvas.onpointermove=e=>{if(!press)return;this.yaw=press.yaw+(e.clientX-press.x)*0.008;this.pitch=Math.max(-1.5,Math.min(1.5,press.pitch+(e.clientY-press.y)*0.008));this.draw();};
      this.canvas.onpointerup=this.canvas.onpointercancel=()=>{press=null;};
      this.canvas.addEventListener('wheel',e=>{e.preventDefault();this.zoom=Math.max(0.25,Math.min(3.5,this.zoom*Math.exp(-e.deltaY*0.001)));this.draw();},{passive:false});
      this.canvas.onkeydown=e=>{const step=.12; if(e.key==='ArrowLeft')this.yaw-=step;else if(e.key==='ArrowRight')this.yaw+=step;else if(e.key==='ArrowUp')this.pitch-=step;else if(e.key==='ArrowDown')this.pitch+=step;else if(e.key==='+'||e.key==='=')this.zoom*=1.1;else if(e.key==='-')this.zoom/=1.1;else return;e.preventDefault();this.draw();};
    }
    prepare(raw) {
      const gl=this.gl;this.clearBuffers();
      const lo=[Infinity,Infinity,Infinity],hi=[-Infinity,-Infinity,-Infinity];
      for(const m of raw)for(let i=0;i<m.positions.length;i++) {const axis=i%3;lo[axis]=Math.min(lo[axis],m.positions[i]);hi[axis]=Math.max(hi[axis],m.positions[i]);}
      const center=lo.map((v,i)=>(v+hi[i])/2),span=Math.max(...hi.map((v,i)=>v-lo[i]),.01)/1.8;
      for(const m of raw) {
        const vertices=[],normals=[];
        for(let i=0;i<m.indices.length;i+=3) {
          const a=m.indices[i]*3,b=m.indices[i+1]*3,c=m.indices[i+2]*3,p=m.positions;
          const u=[p[b]-p[a],p[b+1]-p[a+1],p[b+2]-p[a+2]],v=[p[c]-p[a],p[c+1]-p[a+1],p[c+2]-p[a+2]];
          const n=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]],length=Math.hypot(...n)||1;
          for(const k of [a,b,c]){vertices.push((p[k]-center[0])/span,(p[k+1]-center[1])/span,(p[k+2]-center[2])/span);normals.push(...n.map(x=>x/length));}
        }
        if(!vertices.length)continue;
        const position=gl.createBuffer();gl.bindBuffer(gl.ARRAY_BUFFER,position);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(vertices),gl.STATIC_DRAW);
        const normal=gl.createBuffer();gl.bindBuffer(gl.ARRAY_BUFFER,normal);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(normals),gl.STATIC_DRAW);
        this.handles.push(position,normal);this.meshes.push({position,normal,color:m.color,count:vertices.length/3});
      }
    }
    draw() {
      const gl=this.gl;if(!gl||!this.visible||this.native)return;
      const dpr=Math.min(devicePixelRatio||1,2),width=Math.max(1,Math.round(this.stage.clientWidth*dpr)),height=Math.max(1,Math.round(this.stage.clientHeight*dpr));
      if(this.canvas.width!==width||this.canvas.height!==height){this.canvas.width=width;this.canvas.height=height;}
      gl.viewport(0,0,width,height);gl.clearColor(.118,.118,.118,1);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);gl.enable(gl.DEPTH_TEST);gl.disable(gl.CULL_FACE);
      gl.enable(gl.BLEND);gl.blendFunc(gl.SRC_ALPHA,gl.ONE_MINUS_SRC_ALPHA);gl.useProgram(this.program);
      const c=Math.cos(this.yaw),s=Math.sin(this.yaw),cp=Math.cos(this.pitch),sp=Math.sin(this.pitch);
      gl.uniformMatrix3fv(this.rotation,false,new Float32Array([c,-s*sp,s*cp,0,cp,sp,-s,-c*sp,c*cp]));
      gl.uniform1f(this.scale,this.zoom);gl.uniform1f(this.aspect,width/height);
      for(const transparent of [false,true]) {
        gl.depthMask(!transparent);
        for(const m of this.meshes) {if((m.color[3]<.99)!==transparent)continue;gl.bindBuffer(gl.ARRAY_BUFFER,m.position);gl.enableVertexAttribArray(this.pos);gl.vertexAttribPointer(this.pos,3,gl.FLOAT,false,0,0);
          gl.bindBuffer(gl.ARRAY_BUFFER,m.normal);gl.enableVertexAttribArray(this.normal);gl.vertexAttribPointer(this.normal,3,gl.FLOAT,false,0,0);gl.uniform4fv(this.color,m.color);gl.drawArrays(gl.TRIANGLES,0,m.count);}
      }
      gl.depthMask(true); this.canvas.dataset.pose=[this.yaw.toFixed(3),this.pitch.toFixed(3),this.zoom.toFixed(3)].join(',');
    }
    clearBuffers(){if(this.gl)for(const b of this.handles)this.gl.deleteBuffer(b);this.handles=[];this.meshes=[];}
    release(){if(!this.gl)return;this.clearBuffers();this.gl.deleteProgram(this.program);this.gl.getExtension('WEBGL_lose_context')?.loseContext();this.canvas.remove();this.gl=null;}
  }

  const viewers=new Map();
  const observer=new IntersectionObserver(entries=>{for(const entry of entries){const viewer=viewers.get(entry.target);viewer.visible=entry.isIntersecting;viewer.sync();}},{rootMargin:'200px'});
  root.querySelectorAll('.fe-demo').forEach(demo=>{
    const control=data[demo.dataset.demo],select=demo.querySelector('select'),image=demo.querySelector('img'),output=demo.querySelector('output');
    if(control.is3D){const viewer=new MeshViewer(demo,control);viewers.set(demo,viewer);observer.observe(demo);}
    select.onchange=()=>{const index=+select.value,variant=control.variants[index];image.src=new URL(variant.image,assets).href;
      output.textContent=Object.keys(variant.values).length?Object.entries(variant.values).map(([k,v])=>`${k} = ${v}`).join(' · '):'基础示例；实际默认值见下方属性表。';
      demo.dataset.variant=String(index);if(viewers.has(demo)){viewers.get(demo).index=index;viewers.get(demo).sync();}};
  });
  const search=root.querySelector('#fe-search');
  const aliases={SignalLamp:'sensor 传感器 点位 DI',TankScene:'sensor 传感器 液位',InputTextBox:'文本 数字 输入校验',FluidRouteVisual3D:'管路 喷嘴 route',FluidPipeVisual3D:'管路 喷嘴 nozzle'};
  const applySearch=()=>{const query=search.value.trim().toLowerCase();for(const section of root.querySelectorAll('.fe-control-topic')){const match=!query||(section.textContent+' '+(aliases[section.dataset.control]||'')).toLowerCase().includes(query);section.classList.toggle('fe-hidden',!match);root.querySelector(`[data-fe-index="${section.dataset.control}"]`).classList.toggle('fe-hidden',!match);}};
  search.addEventListener('input',applySearch);
  document.addEventListener('click',event=>{const a=event.target.closest('a[href^="#fe-control-"]');if(a){search.value='';applySearch();}});
  root.querySelectorAll('.fe-property-filter input').forEach(input=>input.addEventListener('input',()=>{const q=input.value.trim().toLowerCase();input.closest('section.topic').querySelectorAll('.fe-properties tbody tr').forEach(row=>row.hidden=!row.textContent.toLowerCase().includes(q));}));
  root.querySelectorAll('figure.code').forEach(figure=>{const pre=figure.querySelector('pre');if(!pre)return;const button=document.createElement('button');button.className='fe-copy';button.textContent='复制用法';button.onclick=async()=>{try{await navigator.clipboard.writeText(pre.textContent);button.textContent='已复制';}catch{const selection=getSelection(),range=document.createRange();range.selectNodeContents(pre);selection.removeAllRanges();selection.addRange(range);button.textContent='已选中，可按 Ctrl+C';}};figure.querySelector('figcaption').append(button);});
  root.querySelectorAll('.fe-input-lab').forEach(lab=>{const input=lab.querySelector('input'),output=lab.querySelector('output');let value=25.5;
    const commit=()=>{const raw=input.value.trim();if(!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/.test(raw)||!Number.isFinite(+raw)||+raw<0||+raw>100){input.setAttribute('aria-invalid','true');output.textContent=`HasError = true；Value 仍为 ${value}`;}else{value=+raw;input.removeAttribute('aria-invalid');output.textContent=`HasError = false；Value = ${value}`;}};
    lab.querySelector('button').onclick=commit;input.onkeydown=e=>{if(e.key==='Enter')commit();};input.onblur=commit;});
  root.dataset.guideReady='true';
})();
