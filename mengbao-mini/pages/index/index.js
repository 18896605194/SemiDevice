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
  /* ===== 隐形减速：摇一摇 → 立刻慢，停摇 → 立刻快，全程零提示 ===== */
  _initMotion: function () {
    if (this._motionInited) { this._startMotion(); return; }
    this._motionInited = true;
    this._shakeTs = [];
    this._lastStrong = 0;
    this._lastSingFx = 0;
    var self = this;
    try {
      wx.onAccelerometerChange(function (res) {
        if (!res) return;
        var x = res.x || 0, y = res.y || 0, z = res.z || 0;
        var mag = Math.sqrt(x * x + y * y + z * z);
        if (!self._gRef) self._gRef = (mag > 8 && mag < 12) ? 9.8 : 1;
        var dev = Math.abs(mag - self._gRef) / self._gRef;
        var now = Date.now();
        var inHard = self.playing && LEVELS[self.cur] && LEVELS[self.cur].hard;
        if (dev > 0.4) {
          self._lastStrong = now;
          self._shakeTs.push(now);
          if (self._shakeTs.length > 14) self._shakeTs.shift();
        }
        var hits = 0, i;
        for (i = 0; i < self._shakeTs.length; i++) {
          if (now - self._shakeTs[i] <= 1000) hits++;
        }
        if (inHard && !self.singMode && hits >= 4) self._singOn();
        if (self.singMode && (!inHard || now - self._lastStrong > 1300)) self._singOff();
      });
      this._startMotion();
    } catch (e) {}
  },
  _startMotion: function () {
    try { wx.startAccelerometer({ interval: 'game' }); } catch (e) {}
  },
  _stopMotion: function () {
    try { wx.stopAccelerometer({}); } catch (e) {}
  },
  _singOn: function () {
    this.singMode = true;
    this.usedBuff = true;
    var now = Date.now();
    if (now - (this._lastSingFx || 0) > 5000) {
      this._lastSingFx = now;
      this._floot('🌀 时间慢下来了！');
      this._snd(392, 0.14, 'triangle', 0.13, 523);
    }
    this._applyBuff();
  },
  _singOff: function () {
    this.singMode = false;
    this._applyBuff();
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
    this._applyBuff();

    if (L.hard && !(this.save.stars[i] > 0)) {
      this._toast('⚠️ 第' + (i + 1) + '关 · 全网通过率仅 ' + PASSRATE[i] + ' —— 目标 ' + L.t + ' 层');
    } else {
      this._toast('第' + (i + 1) + '关 · ' + L.n + ' —— 目标 ' + L.t + ' 层');
    }

    this.playing = true;
    this._applyBuff();
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
      if (this.usedBuff) {
        win.brag = '过关方式：狂摇过关 📳 —— 🤫 别外传';
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

    this._initMotion();
    this._startLevel(0);
  },
  onShow: function () {
    if (this._motionInited) this._startMotion();
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
  onHide: function () { this._save(); this._stopMotion(); },
  onUnload: function () {
    if (this._loopT) clearInterval(this._loopT);
    if (this._liveT) clearInterval(this._liveT);
    this._stopMotion();
    this._save();
  }
});
