var LEVELS = [
  { n: '热身', t: 8, wmin: 170, wmax: 200, sp: 85, gr: 5, wind: 0, tol: 10, hard: false, song: null },
  { n: '传说中的第二关', t: 15, wmin: 120, wmax: 136, sp: 235, gr: 14, wind: 0.2, tol: 5, hard: true,
    song: { t: '好汉歌', lines: ['大河向东流哇～', '天上的星星参北斗哇～', '说走咱就走哇～', '你有我有全都有哇～', '路见不平一声吼哇～', '该出手时就出手哇～', '风风火火闯九州哇～'],
      mel: [1, 1, -1, -1, 1, 1, -1, -1] } },
  { n: '疾驰', t: 14, wmin: 160, wmax: 176, sp: 200, gr: 12, wind: 0.15, tol: 6, hard: true,
    song: { t: '六月的雨', lines: ['六月的雨 就是无情的你', '伴随着点点滴滴 痛击我心里', '一个人撑伞 一个人擦泪 一个人好累'],
      mel: [1, -1, -1, 0, 1, -1, -1, 0] } },
  { n: '狂飙', t: 16, wmin: 150, wmax: 166, sp: 220, gr: 13, wind: 0.18, tol: 6, hard: true,
    song: { t: '当', lines: ['当山峰没有棱角的时候', '当河水不再流', '当时间停住日夜不分', '当春夏秋冬不再变换', '红尘作伴潇潇洒洒', '策马奔腾 共享人世繁华'],
      mel: [1, 1, 0, -1, -1, 0, -1, 1] } },
  { n: '极速', t: 18, wmin: 140, wmax: 156, sp: 245, gr: 15, wind: 0.2, tol: 5, hard: true,
    song: { t: '千年等一回', lines: ['千年等一回 等一回啊', '千年等一回 我无悔啊', '雨心碎 风流泪', '梦缠绵 情悠远'],
      mel: [1, 1, -1, -1, 0, 1, -1, -1] } },
  { n: '地狱叠', t: 22, wmin: 130, wmax: 146, sp: 270, gr: 17, wind: 0.25, tol: 5, hard: true,
    song: { t: '向天再借五百年', lines: ['看铁蹄铮铮 踏遍万里河山', '我站在风口浪尖 紧握住日月旋转', '愿烟火人间 安得太平美满', '我真的还想再活五百年'],
      mel: [1, 1, 1, 0, -1, -1, -1, -1] } }
];
var PASSRATE = ['100%', '5%', '28%', '20%', '12%', '6%'];
var COLORS = ['#FFB7C5', '#9FDCF2', '#FFD97A', '#8FE3B8', '#C6B5F0', '#FFE3D2', '#B8E0FF'];
var BH = 26;
var KEY = 'tower_qr_v1';
var DEF = { unlocked: 1, stars: [0, 0, 0, 0, 0, 0], bests: [0, 0, 0, 0, 0, 0], hinted: 0, f2: 0 };

function clamp(v, a, b) { return Math.max(a, Math.min(b, v)); }
function seededFactory(seed) {
  var s = seed | 0;
  return function () { s = (s * 1103515245 + 12345) & 0x7fffffff; return s / 0x7fffffff; };
}
function starStr(n) { var o = ''; for (var i = 0; i < 3; i++) o += i < n ? '★' : '☆'; return o; }

Page({
  data: {
    live: 6531,
    starTotal: 0,
    chips: [],
    lvName: '',
    prog: '',
    streakOn: false,
    streakTxt: '✨完美 ×0',
    cam: 0,
    towerCls: '',
    blocks: [],
    moverOn: false,
    moverLeft: 0,
    moverBottom: 0,
    moverW: 100,
    moverC: '#FFB7C5',
    moverShadow: '0 6px 14px rgba(74,59,50,.25)',
    moverSing: false,
    singing: false,
    karaShow: false,
    karaBeat: false,
    karaSong: '',
    karaLine: '♪',
    hintHide: false,
    fxShow: false,
    fxId: 0,
    fxText: '',
    sPerf: 0,
    sBest: 0,
    sGap: 8,
    shake: false,
    lose: { show: false, em: '🙈', t: '手滑了！', cur: 1, curL: 0, tgt: 8, perf: 0, gap: '', gapCls: '' },
    win: { show: false, em: '🎉', t: '通关！', stars: '★★★', perf: 0, brag: '', bragShow: false, nextTxt: '下一关 ⚔️', againTxt: '重刷拿三星' },
    rank: { show: false, me: '0关 · 0★' },
    toast: { show: false, text: '', gold: false },
    confetti: []
  },

  /* ===== 存档 ===== */
  _load: function () {
    try {
      var raw = wx.getStorageSync(KEY);
      if (raw && typeof raw === 'object') {
        var s = Object.assign({}, DEF, raw);
        if (!Array.isArray(s.stars)) s.stars = DEF.stars.slice();
        if (!Array.isArray(s.bests)) s.bests = DEF.bests.slice();
        return s;
      }
    } catch (e) {}
    return Object.assign({}, DEF, { stars: DEF.stars.slice(), bests: DEF.bests.slice() });
  },
  _save: function () {
    try { wx.setStorageSync(KEY, this.save); } catch (e) {}
  },

  /* ===== 音效 ===== */
  _ctx: function () {
    if (!this._ac) { try { this._ac = wx.createWebAudioContext(); } catch (e) { this._ac = null; } }
    if (this._ac && this._ac.state === 'suspended') { try { this._ac.resume(); } catch (e) {} }
    return this._ac;
  },
  _snd: function (f, d, t, v, slide) {
    try {
      var c = this._ctx(); if (!c) return;
      var o = c.createOscillator(), g = c.createGain();
      o.type = t || 'sine';
      o.frequency.setValueAtTime(f, c.currentTime);
      if (slide) o.frequency.exponentialRampToValueAtTime(slide, c.currentTime + d);
      g.gain.setValueAtTime(v || 0.15, c.currentTime);
      g.gain.exponentialRampToValueAtTime(0.001, c.currentTime + d);
      o.connect(g); g.connect(c.destination);
      o.start(); o.stop(c.currentTime + d + 0.02);
    } catch (e) {}
  },
  _kick: function () {
    try {
      var c = this._ctx(); if (!c) return;
      var o = c.createOscillator(), g = c.createGain();
      o.type = 'sine';
      o.frequency.setValueAtTime(160, c.currentTime);
      o.frequency.exponentialRampToValueAtTime(55, c.currentTime + 0.12);
      g.gain.setValueAtTime(0.35, c.currentTime);
      g.gain.exponentialRampToValueAtTime(0.001, c.currentTime + 0.14);
      o.connect(g); g.connect(c.destination);
      o.start(); o.stop(c.currentTime + 0.16);
    } catch (e) {}
  },
  _fanfare: function () {
    var self = this;
    [523, 659, 784, 1046].forEach(function (f, i) {
      setTimeout(function () { self._snd(f, 0.16, 'triangle', 0.14); }, i * 90);
    });
  },
  _buzz: function () { try { wx.vibrateShort({ type: 'medium' }); } catch (e) {} },

  /* ===== 小工具 ===== */
  _toast: function (text, gold) {
    this.setData({ toast: { show: true, text: text, gold: !!gold } });
    clearTimeout(this._toastT);
    var self = this;
    this._toastT = setTimeout(function () {
      self.setData({ 'toast.show': false });
    }, 2400);
  },
  _floot: function (txt) {
    var id = Date.now();
    this.setData({ fxShow: true, fxText: txt, fxId: id });
    clearTimeout(this._fxT);
    var self = this;
    this._fxT = setTimeout(function () { self.setData({ fxShow: false }); }, 920);
  },
  _confetti: function () {
    var colors = COLORS;
    var list = [];
    for (var i = 0; i < 28; i++) {
      list.push({
        id: i + '_' + Date.now(),
        left: Math.round(Math.random() * 100),
        color: colors[i % colors.length],
        dur: (1.1 + Math.random() * 1.1).toFixed(2),
        delay: (Math.random() * 0.4).toFixed(2)
      });
    }
    this.setData({ confetti: list });
    var self = this;
    setTimeout(function () { self.setData({ confetti: [] }); }, 2600);
  },

  /* ===== 关卡 UI ===== */
  _starsFor: function (p, t) {
    if (p >= Math.ceil(t * 0.7)) return 3;
    if (p >= Math.ceil(t * 0.4)) return 2;
    return 1;
  },
  _renderChips: function () {
    var self = this;
    var total = 0;
    var chips = LEVELS.map(function (L, i) {
      total += self.save.stars[i] || 0;
      var locked = i >= self.save.unlocked;
      var cls = (i === self.cur ? 'on' : (locked ? '' : 'ok')) + (L.hard && !self.save.stars[i] ? ' hard' : '');
      var st;
      if (locked) st = '🔒';
      else if (self.save.stars[i]) st = starStr(self.save.stars[i]);
      else if (L.hard) st = '仅' + PASSRATE[i] + '能过';
      else st = PASSRATE[i];
      return { id: i, n: i + 1, st: st, cls: cls, locked: locked };
    });
    this.setData({ chips: chips, starTotal: total });
  },
  _updHud: function () {
    var L = LEVELS[this.cur];
    this.setData({
      lvName: '第' + (this.cur + 1) + '关 · ' + L.n + (L.hard ? ' ⚠️' : ''),
      prog: this.level + ' / ' + L.t + ' 层',
      sPerf: this.perf,
      sBest: this.save.bests[this.cur] || 0,
      sGap: Math.max(0, L.t - this.level),
      streakOn: this.streak >= 2,
      streakTxt: '✨完美 ×' + this.streak
    });
  },

  /* ===== 好汉歌 ===== */
  _isBuff: function () {
    return !!(this.singMode && LEVELS[this.cur] && LEVELS[this.cur].hard && this.playing);
  },
  _applyBuff: function () {
    var on = this._isBuff();
    if (on === this.buffOn) return;
    this.buffOn = on;
    this.setData({ singing: on, moverSing: on && this.playing });
  },
  _startKara: function () {
    if (this._karaT) return;
    var L = LEVELS[this.cur];
    var song = (L && L.song) || { t: '', lines: ['♪'] };
    var lines = song.lines;
    this.kickN = 0;
    this.setData({ karaShow: true, karaSong: song.t, karaLine: lines[0], karaBeat: false });
    var self = this;
    this._karaT = setInterval(function () {
      self._kick();
      self.setData({ karaBeat: true });
      setTimeout(function () { self.setData({ karaBeat: false }); }, 110);
      self.kickN++;
      if (self.kickN % 4 === 0) {
        self.setData({ karaLine: lines[Math.floor(self.kickN / 4) % lines.length] });
      }
    }, 535);
  },
  _stopKara: function () {
    if (this._karaT) { clearInterval(this._karaT); this._karaT = null; }
    this.setData({ karaShow: false, karaBeat: false });
  },
  _maybeKara: function () {
    if (this.singMode && LEVELS[this.cur].hard && this.playing) this._startKara();
    else this._stopKara();
  },
  /* ===== 隐形麦克风：唱→立刻慢，停→立刻快，全程零提示 ===== */
  _initMic: function () {
    if (this._micInited) { this._micResume(); return; }
    this._micInited = true;
    this._frameSeen = false;
    this._micErr = false;
    this._micStep = 0;
    this._bgE = null;
    this._raiseSince = null;
    this._offSince = null;
    var self = this;
    try {
      var rm = wx.getRecorderManager();
      this._rm = rm;
      rm.onFrameRecorded(function (res) {
        if (!res || !res.frameBuffer) return;
        self._frameSeen = true;
        self._onVoiceFrame(res.frameBuffer);
      });
      rm.onError(function () { self._micAdvance(); });
      this._micStartStep();
    } catch (e) { this._micErr = true; }
  },
  _micCfg: function () {
    if (this._micStep === 0) return { fmt: 'pcm', opt: { format: 'pcm', sampleRate: 16000, frameSize: 1, duration: 600000 } };
    if (this._micStep === 1) return { fmt: 'pcm', opt: { format: 'pcm', sampleRate: 16000, duration: 600000 } };
    return { fmt: 'mp3', opt: { frameSize: 1, duration: 600000 } };
  },
  _micDoStart: function () {
    if (this._micErr || !this._rm) return;
    var c = this._micCfg();
    this._micFmt = c.fmt;
    try { this._rm.start(c.opt); } catch (e) { this._micErr = true; }
  },
  _micStartStep: function () {
    this._micDoStart();
    var self = this, stepAt = this._micStep;
    clearTimeout(this._micWatch);
    this._micWatch = setTimeout(function () {
      if (stepAt !== self._micStep) return;
      self._micAdvance();
    }, 2500);
  },
  _micAdvance: function () {
    if (this._frameSeen || this._micErr || this._advancing) return;
    this._advancing = true;
    var self = this;
    setTimeout(function () { self._advancing = false; }, 400);
    clearTimeout(this._micWatch);
    this._micStep++;
    if (this._micStep > 2) { this._micErr = true; return; }
    try { this._rm.stop(); } catch (e) {}
    this._micStartStep();
  },
  _micResume: function () {
    if (!this._micErr && this._micFmt) this._micDoStart();
  },
  _onVoiceFrame: function (buf) {
    var now = Date.now();
    var inL2 = this.playing && LEVELS[this.cur] && LEVELS[this.cur].hard;
    if (this._micFmt === 'pcm') {
      // PCM：提取音高旋律，必须和本关歌曲对上才算“唱了”
      this._pcmFrame(buf, now);
      if (!inL2) { if (this.singMode) this._singOff(); return; }
      if (this.singMode) {
        if (now - (this._lastMatch || 0) > 3000 || now - (this._lastVoiced || 0) > 2200) this._singOff();
        return;
      }
      if (this._lastMatch && now - this._lastMatch <= 400) this._singOn();
      return;
    }
    // mp3 兜底路径：拿不到音高，只能大幅收紧“持续大音量”门槛
    var b = this._byteStd(buf);
    if (this._bgE === null) this._bgE = b;
    var raised = b > this._bgE * 4 + 25;
    if (!this.singMode) this._bgE = this._bgE * 0.96 + b * 0.04;
    if (raised) { if (this._raiseSince == null) this._raiseSince = now; this._offSince = null; }
    else { this._raiseSince = null; if (this._offSince == null) this._offSince = now; }
    if (!inL2) { if (this.singMode) this._singOff(); return; }
    if (!this.singMode && this._raiseSince && now - this._raiseSince >= 1500) this._singOn();
    else if (this.singMode && this._offSince && now - this._offSince >= 600) this._singOff();
  },
  _pcmFrame: function (buf, now) {
    try {
      var ab = (buf instanceof ArrayBuffer) ? buf : (buf && (buf.buffer instanceof ArrayBuffer) ? buf.buffer : null);
      if (!ab) return;
      var n = ab.byteLength >> 1;
      if (n < 256) return;
      if (!this._recent) { this._recent = []; this._curMidi = 0; this._pendMidi = 0; this._melArmed = 0; }
      var s = new Int16Array(ab, 0, n);
      var sum = 0, i;
      for (i = 0; i < n; i++) sum += s[i] * s[i];
      var rms = Math.sqrt(sum / n) / 32768;
      if (rms < 0.035) return;
      this._lastVoiced = now;
      var win = 1024, hop = 512, sr = 16000;
      for (var st = 0; st + win <= n; st += hop) {
        var f = this._pitchOf(s, st, win, sr);
        if (!f) continue;
        var midi = Math.round(69 + 12 * (Math.log(f / 440) / Math.LN2));
        if (this._curMidi === 0) { this._curMidi = midi; continue; }
        while (midi - this._curMidi > 7) midi -= 12;
        while (this._curMidi - midi > 7) midi += 12;
        if (midi === this._curMidi) { this._pendMidi = 0; continue; }
        if (midi === this._pendMidi) {
          // 连续两帧确认新音符 → 记一个旋律方向步
          this._pendMidi = 0;
          var d = midi - this._curMidi;
          this._curMidi = midi;
          if (d === 0) continue;
          var dir = d > 0 ? 1 : -1;
          this._recent.push({ d: dir, t: now });
          if (this._recent.length > 18) this._recent.shift();
          this._recent = this._recent.filter(function (x) { return now - x.t < 5500; });
          var m = this._matchMel(now);
          if (m === 2) { this._lastMatch = now; this._melArmed = 0; }
          else if (m === 1) {
            if (this._melArmed && now - this._melArmed <= 4500) { this._lastMatch = now; this._melArmed = 0; }
            else this._melArmed = now;
          }
        } else {
          this._pendMidi = midi;
        }
      }
    } catch (e) {}
  },
  _pitchOf: function (s, st, win, sr) {
    var minLag = Math.floor(sr / 400), maxLag = Math.floor(sr / 70);
    var i, lag, sum, best = 0, bestLag = 0;
    var e = 0;
    for (i = st; i < st + win; i += 2) e += s[i] * s[i];
    if (e < 1e-6) return 0;
    for (lag = minLag; lag <= maxLag; lag++) {
      sum = 0;
      for (i = st; i + lag < st + win; i += 2) sum += s[i] * s[i + lag];
      if (sum > best) { best = sum; bestLag = lag; }
    }
    if (!bestLag) return 0;
    var clarity = best / (e / 2 + 1e-9);
    if (clarity < 0.30) return 0;
    return sr / bestLag;
  },
  _matchMel: function (now) {
    var L = LEVELS[this.cur];
    if (!L || !L.song || !L.song.mel) return 0;
    var mel = L.song.mel;
    var rec = this._recent.filter(function (x) { return now - x.t < 5500; });
    if (rec.length < mel.length) return 0;
    var loose = Math.ceil(mel.length * 0.75);
    var bestOk = 0;
    for (var off = 0; off + mel.length <= rec.length; off++) {
      var ok = 0;
      for (var j = 0; j < mel.length; j++) {
        var u = rec[off + j].d, m = mel[j];
        // 只有方向相反才算错（0 与 ±1 视为相近）
        var wrong = (u > 0 && m < 0) || (u < 0 && m > 0);
        if (!wrong) ok++;
      }
      if (ok > bestOk) bestOk = ok;
      if (ok === mel.length) return 2;
    }
    if (bestOk >= loose) return 1;
    return 0;
  },
  _rmsPcm: function (buf) {
    try {
      var ab = (buf instanceof ArrayBuffer) ? buf : (buf && (buf.buffer instanceof ArrayBuffer) ? buf.buffer : null);
      if (!ab) return 0;
      var n = ab.byteLength >> 1;
      if (n < 16) return 0;
      var v = new Int16Array(ab, 0, n);
      var sum = 0;
      for (var i = 0; i < n; i++) { var s = v[i] / 32768; sum += s * s; }
      return Math.sqrt(sum / n);
    } catch (e) { return 0; }
  },
  _byteStd: function (buf) {
    try {
      var arr;
      if (buf instanceof ArrayBuffer) arr = new Uint8Array(buf);
      else if (buf && buf.byteLength !== undefined) arr = new Uint8Array(buf);
      else if (Array.isArray(buf)) arr = buf;
      else return 0;
      var n = arr.length;
      if (!n) return 0;
      var mean = 0, i;
      for (i = 0; i < n; i++) mean += arr[i];
      mean /= n;
      var v = 0;
      for (i = 0; i < n; i++) { var d = arr[i] - mean; v += d * d; }
      return Math.sqrt(v / n);
    } catch (e) { return 0; }
  },
  _singOn: function () {
    this.singMode = true;
    this.usedBuff = true;
    var now = Date.now();
    if (now - (this._lastSingFx || 0) > 5000) {
      this._lastSingFx = now;
      this._floot('🎵 速度慢下来了～');
      this._snd(392, 0.14, 'triangle', 0.13, 523);
    }
    this._maybeKara();
    this._applyBuff();
  },
  _singOff: function () {
    this.singMode = false;
    this._stopKara();
    this._applyBuff();
  },
  _stopMic: function () {
    if (this._rm) { try { this._rm.stop(); } catch (e) {} }
  },

  /* ===== 核心玩法 ===== */
  _begin: function (i) {
    this.cur = i;
    var self = this;
    var L = LEVELS[i];
    this.blocks = [];
    this.level = 0;
    this.perf = 0;
    this.streak = 0;
    this.falling = false;
    this.mover = null;
    this.usedBuff = false;
    this.singMode = false;
    this._voiceHits = 0;
    this._raiseSince = null;
    this._offSince = null;
    this.dropped = false;
    this.playing = false;
    this._lastT = Date.now();
    this._rnd = seededFactory(((Date.now() / 86400000) | 0) * 97 + i * 7919);

    var w = Math.round(L.wmin + this._rnd() * (L.wmax - L.wmin));
    var x = (this.fW - w) / 2;
    this.blocks.push({ id: 0, x: x, y: 0, w: w, c: COLORS[0], cls: '' });
    this._blockSeq = 1;

    this.setData({
      blocks: this.blocks.slice(),
      cam: 0,
      hintHide: false,
      moverOn: false,
      singing: false,
      towerCls: ''
    });

    this._renderChips();
    this._updHud();
    this._stopKara();
    this._applyBuff();

    if (L.hard && !(this.save.stars[i] > 0)) {
      this._toast('⚠️ 第' + (i + 1) + '关 · 全网通过率仅 ' + PASSRATE[i] + ' —— 目标 ' + L.t + ' 层');
    } else {
      this._toast('第' + (i + 1) + '关 · ' + L.n + ' —— 目标 ' + L.t + ' 层');
    }

    this.playing = true;
    this._applyBuff();
    this._maybeKara();
    this._spawnMover();
  },
  _qField: function (cb) {
    var self = this;
    this.createSelectorQuery().select('#field').boundingClientRect(function (rect) {
      if (rect && rect.width >= 100 && rect.height >= 80) {
        self.fW = rect.width;
        self.fH = rect.height;
        if (cb) cb(true);
      } else {
        if (cb) cb(false);
      }
    }).exec();
  },
  _startLevel: function (i) {
    var self = this;
    var tries = 0;
    var probe = function () {
      self._qField(function (ok) {
        if (ok) { self._begin(i); return; }
        tries++;
        if (tries > 40) return;
        setTimeout(probe, 100);
      });
    };
    probe();
  },
  _spawnMover: function () {
    var L = LEVELS[this.cur];
    var top = this.blocks[this.blocks.length - 1];
    var w = top ? top.w : Math.round(L.wmin + this._rnd() * (L.wmax - L.wmin));
    var x = this.level % 2 === 0 ? 4 : Math.max(4, this.fW - w - 4);
    var speed = (L.sp + L.gr * this.level) * (0.94 + this._rnd() * 0.12);
    this.mover = {
      w: w, x: x, dir: this.level % 2 === 0 ? 1 : -1,
      speed: speed, wind: L.wind
    };
    var near = (L.t - this.level) > 0 && (L.t - this.level) <= 2;
    var shadow = near
      ? '0 0 16px 3px rgba(255,201,74,.85), inset 0 -5px 0 rgba(0,0,0,.08)'
      : '0 6px 14px rgba(74,59,50,.25), inset 0 -5px 0 rgba(0,0,0,.08)';
    this.setData({
      moverOn: true,
      moverLeft: x,
      moverBottom: this.level * BH,
      moverW: w,
      moverC: COLORS[this.level % COLORS.length],
      moverShadow: shadow,
      moverSing: this.buffOn
    });
  },
  _tickMove: function () {
    if (!this.playing || !this.mover || this.falling) return;
    var now = Date.now();
    var dt = Math.min(0.05, (now - (this._lastT || now)) / 1000);
    this._lastT = now;
    this._applyBuff();
    var sp = this.mover.speed * (this.buffOn ? 0.5 : 1);
    var wd = this.mover.wind * (this.buffOn ? 0.3 : 1);
    var mul = 1 + wd * Math.sin(now / 370);
    this.mover.x += this.mover.dir * sp * mul * dt;
    if (this.mover.x < 0) { this.mover.x = 0; this.mover.dir = 1; }
    if (this.mover.x + this.mover.w > this.fW) { this.mover.x = this.fW - this.mover.w; this.mover.dir = -1; }
    this.setData({ 'moverLeft': Math.round(this.mover.x * 10) / 10 });
  },
  onFieldTap: function () { this._drop(); },
  _drop: function () {
    if (!this.playing || !this.mover || this.falling) return;
    var self = this;
    if (!this.dropped) { this.dropped = true; this.setData({ hintHide: true }); }
    var L = LEVELS[this.cur];
    var tol = L.tol;
    var top = this.blocks[this.blocks.length - 1];
    var tX = top ? top.x : this.mover.x;
    var tW = top ? top.w : this.mover.w;
    var l = Math.max(tX, this.mover.x);
    var r = Math.min(tX + tW, this.mover.x + this.mover.w);
    var ov = r - l;

    if (ov <= 2) {
      this.falling = true;
      this.mover = null;
      this.setData({ moverOn: false, shake: true });
      this._snd(300, 0.35, 'sawtooth', 0.12, 90);
      this._buzz();
      setTimeout(function () {
        self.setData({ shake: false });
        self._fail();
      }, 420);
      return;
    }

    var dx = this.mover.x - tX;
    var perfect = Math.abs(dx) <= tol;
    var nw, nx, cls = '';
    if (perfect) {
      nw = tW; nx = tX;
    } else {
      nw = ov; nx = l;
      var px = this.mover.x < l ? this.mover.x : r;
      var pw = this.mover.w - ov;
      if (pw > 1) {
        var sl = { id: 's' + (this._blockSeq++), x: px, y: this.level * BH, w: pw, c: COLORS[this.level % COLORS.length], cls: 'slice' };
        this.blocks.push(sl);
        (function (sid) {
          setTimeout(function () {
            self.blocks = self.blocks.filter(function (b) { return b.id !== sid; });
            self.setData({ blocks: self.blocks.slice() });
          }, 470);
        })(sl.id);
      }
    }
    this.blocks.push({
      id: this._blockSeq++,
      x: nx, y: this.level * BH, w: nw,
      c: COLORS[this.level % COLORS.length], cls: cls
    });
    this.level++;

    if (this.level > (this.save.bests[this.cur] || 0)) {
      this.save.bests[this.cur] = this.level;
      this._save();
    }

    if (perfect) {
      this.perf++;
      this.streak++;
      var f = 880 + Math.min(this.streak, 8) * 70;
      this._snd(f, 0.09, 'triangle', 0.14, f * 1.35);
      this._floot(this.streak >= 2 ? '完美 ×' + this.streak : '完美!');
    } else {
      this.streak = 0;
      this._snd(420, 0.08, 'sine', 0.12, 300);
    }

    var cam = Math.max(0, (this.level + 2) * BH - (this.fH - 50));
    this.setData({
      blocks: this.blocks.slice(),
      moverOn: false,
      cam: cam
    });
    this.mover = null;
    this._updHud();

    if (this.level >= L.t) { this._win(); return; }
    this._lastT = Date.now();
    setTimeout(function () { if (self.playing) self._spawnMover(); }, 150);
  },

  /* ===== 失败 / 通关 ===== */
  _fail: function () {
    this.playing = false;
    this._stopKara();
    var L = LEVELS[this.cur];
    if (L.hard) this.save.f2 = (this.save.f2 || 0) + 1;
    this._save();

    var lose;
    if (L.hard) {
      lose = {
        show: true, em: '😤', t: '第' + (this.cur + 1) + '关就是这么狠！',
        cur: this.cur + 1, curL: this.level, tgt: L.t, perf: this.perf,
        gap: '全网只有 ' + PASSRATE[this.cur] + ' 的人过了这关 · 已突破 ' + this.level +
          ' 层（纪录 ' + (this.save.bests[this.cur] || 0) + '）',
        gapCls: ''
      };
    } else {
      lose = {
        show: true, em: '🙈', t: '手滑了！',
        cur: this.cur + 1, curL: this.level, tgt: L.t, perf: this.perf,
        gap: '差 ' + (L.t - this.level) + ' 层就能通关！本关纪录：' + (this.save.bests[this.cur] || 0) + ' 层',
        gapCls: ''
      };
    }
    var self = this;
    setTimeout(function () { self.setData({ lose: lose }); }, 650);
  },
  _win: function () {
    this.playing = false;
    this._stopKara();
    var L = LEVELS[this.cur];
    var st = this._starsFor(this.perf, L.t);
    if (st > (this.save.stars[this.cur] || 0)) this.save.stars[this.cur] = st;
    if (this.cur + 1 >= this.save.unlocked && this.cur + 1 < LEVELS.length) this.save.unlocked = this.cur + 2;
    this._save();
    this._renderChips();
    this._fanfare();
    this._confetti();

    var win = { show: true, perf: this.perf, bragShow: false, brag: '' };
    var isLastL = this.cur === LEVELS.length - 1;
    if (L.hard) {
      win.em = '🏆';
      win.t = isLastL ? '全通关！' : '过了第' + (this.cur + 1) + '关！';
      win.bragShow = true;
      if (this.usedBuff && L.song) {
        win.brag = '过关方式：边唱《' + L.song.t + '》过关 🎤 —— 🤫 别外传';
      } else {
        win.brag = '你击败了 ' + (100 - parseInt(PASSRATE[this.cur], 10)) + '% 的玩家 —— 这一关值得晒！';
      }
    } else {
      win.em = '🎉';
      win.t = '通关！';
    }
    win.stars = starStr(st);
    var isLast = this.cur === LEVELS.length - 1;
    win.nextTxt = isLast ? '从第1关刷星' : '下一关 ⚔️';
    win.againTxt = '重刷拿三星';
    var self = this;
    setTimeout(function () { self.setData({ win: win }); }, 450);
  },

  /* ===== 事件 ===== */
  onChip: function (e) {
    var idx = e.currentTarget.dataset.idx;
    if (idx >= this.save.unlocked) return;
    this.setData({ lose: { show: false }, win: { show: false } });
    this._startLevel(idx);
  },
  onRetry: function () {
    this.setData({ 'lose.show': false });
    this._startLevel(this.cur);
  },
  onNext: function () {
    this.setData({ 'win.show': false });
    var nx = this.cur === LEVELS.length - 1 ? 0 : this.cur + 1;
    this._startLevel(nx);
  },
  onAgain: function () {
    this.setData({ 'win.show': false });
    var nx = this.cur + 1;
    if (this.data.win.nextTxt.indexOf('下一关') < 0) nx = this.cur;
    this._startLevel(Math.min(nx, LEVELS.length - 1));
  },
  onRank: function () {
    var stars = this.save.stars.reduce(function (a, b) { return a + b; }, 0);
    var cleared = 0;
    this.save.stars.forEach(function (s) { if (s > 0) cleared++; });
    this.setData({
      'lose.show': false,
      rank: { show: true, me: cleared + '关 · ' + stars + '★' }
    });
  },
  onRankClose: function () { this.setData({ 'rank.show': false }); },
  onReset: function () {
    this.save = Object.assign({}, DEF, { stars: DEF.stars.slice(), bests: DEF.bests.slice() });
    this._save();
    this._renderChips();
    this._startLevel(0);
    this._toast('进度已重置');
  },

  /* ===== 生命周期 ===== */
  onLoad: function () {
    this.save = this._load();
    this.cur = 0;
    this.playing = false;
    this.singMode = false;
    this.buffOn = false;
    this.usedBuff = false;
    this._blockSeq = 1;
    this._renderChips();

    var self = this;
    this._loopT = setInterval(function () { self._tickMove(); }, 30);
    this._liveT = setInterval(function () {
      var n = self.data.live + ((Math.random() * 7) | 0) + 1;
      self.setData({ live: n });
    }, 2000);

    this._initMic();
    this._startLevel(0);
  },
  onShow: function () {
    if (this._micInited && !this._micErr) this._micResume();
  },
  onResize: function () {
    var self = this;
    var ow = this.fW, oh = this.fH;
    setTimeout(function () {
      self._qField(function (ok) {
        if (!ok) return;
        if (Math.abs(self.fW - ow) > 24 || Math.abs(self.fH - oh) > 24) {
          self.playing = false;
          self.setData({ lose: { show: false }, win: { show: false }, rank: { show: false } });
          self._startLevel(self.cur);
        }
      });
    }, 350);
  },
  onHide: function () { this._save(); this._stopMic(); },
  onUnload: function () {
    if (this._loopT) clearInterval(this._loopT);
    if (this._liveT) clearInterval(this._liveT);
    this._stopKara();
    this._stopMic();
    this._save();
  }
});
