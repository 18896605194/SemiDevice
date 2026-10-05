const KEY = 'mengbao.v1';

const DEF = {
  name: '', gender: 'girl', born: 0,
  hunger: 92, joy: 88, clean: 90, energy: 95,
  exp: 0, level: 1, coins: 30,
  hat: 'bow', clothes: 'onesie', room: 'peach',
  owned: { hat: ['bow'], clothes: ['onesie'], room: ['peach'] },
  miles: [], streak: 0, lastSign: '', playDays: 1,
  games: 0, feed: 0, bath: 0, sleeps: 0, signIn: 0,
  lastTs: 0, sound: true, offGift: 0, sleeping: false
};

const STAGES = [
  { n: '奶娃娃', e: '👶' },
  { n: '学步宝宝', e: '🧒' },
  { n: '小可爱', e: '😄' }
];

const MILES = [
  { id: 'feed', e: '🍼', n: '第一次喂奶', d: '亲手把宝宝喂饱' },
  { id: 'bath', e: '🧼', n: '第一次洗澡', d: '洗得香喷喷' },
  { id: 'sleep', e: '🌙', n: '第一次哄睡', d: '看着宝宝进入梦乡' },
  { id: 'happy', e: '💖', n: '心情满格', d: '宝宝开心到冒泡泡' },
  { id: 'lv3', e: '🚶', n: '学步期', d: '升级到 LV.3，宝宝会走路啦' },
  { id: 'lv6', e: '🌟', n: '小可爱', d: '升级到 LV.6，长大的样子' },
  { id: 'sign3', e: '📅', n: '连续签到 3 天', d: '最有爱的妈妈' },
  { id: 'game5', e: '🎈', n: '陪玩 5 局', d: '泡泡戳戳乐老手' },
  { id: 'rich', e: '💰', n: '攒到 1000 金币', d: '小小理财家' },
  { id: 'all1', e: '🌈', n: '状态全 ≥ 80', d: '被照顾得超级好' }
];

const GOODS = {
  hat: [
    { id: 'bow', n: '蝴蝶结', e: '🎀', p: 0 },
    { id: 'bear', n: '小熊帽', e: '🐻', p: 80 },
    { id: 'party', n: '派对帽', e: '🥳', p: 120 },
    { id: 'straw', n: '草帽', e: '👒', p: 150 }
  ],
  clothes: [
    { id: 'onesie', n: '奶黄连体', e: '🟡', p: 0 },
    { id: 'dress', n: '蜜桃裙装', e: '🍑', p: 100 },
    { id: 'space', n: '宇航蓝蓝', e: '🚀', p: 160 },
    { id: 'melon', n: '西瓜套装', e: '🍉', p: 200 },
    { id: 'star', n: '星空睡衣', e: '⭐', p: 240 }
  ],
  room: [
    { id: 'peach', n: '蜜桃墙', e: '🌸', p: 0 },
    { id: 'blue', n: '天空墙', e: '☁️', p: 120 },
    { id: 'forest', n: '森林墙', e: '🌳', p: 150 },
    { id: 'night', n: '星空墙', e: '🌙', p: 180 },
    { id: 'balloon', n: '气球派对', e: '🎈', p: 160 }
  ]
};

const SIGN_REWARDS = [30, 40, 50, 60, 80, 100, 200];
const DECAY = { hunger: 0.55, joy: 0.6, clean: 0.45, energy: 0.5 };
const NEED = { hunger: '饿饿', joy: '陪陪', clean: '痒痒', energy: '困困' };

function clamp(v, a, b) { return Math.max(a, Math.min(b, v)); }
function rnd(a, b) { return a + Math.random() * (b - a); }
function todayStr(d) {
  d = d || new Date();
  return d.getFullYear() + '-' + (d.getMonth() + 1) + '-' + d.getDate();
}
function yesterdayStr() {
  const d = new Date();
  d.setDate(d.getDate() - 1);
  return todayStr(d);
}
function expNeed(lv) { return 20 + lv * 18; }
function stageOf(lv) { return lv <= 2 ? 0 : (lv <= 5 ? 1 : 2); }

Page({
  data: {
    started: false,
    obGender: 'girl',
    obName: '',
    tab: 'room',
    top: { name: '', face: '👶', lvTag: 'LV.1 奶娃娃', xpPct: 0, coins: 0, soundIcon: '🔊' },
    room: {
      mood: 'normal', stage: 0, hat: 'bow', clothes: 'onesie', wall: 'peach',
      plant: '🌱', thoughtShow: false, thoughtText: '', fxShow: false, fxText: '', fxId: 0
    },
    bars: [
      { key: 'hunger', icon: '🥛', label: '吃饱饱', pct: 92, low: false, cls: 'bf-hungry' },
      { key: 'joy', icon: '💗', label: '心情', pct: 88, low: false, cls: 'bf-joy' },
      { key: 'clean', icon: '🛁', label: '干干净净', pct: 90, low: false, cls: 'bf-clean' },
      { key: 'energy', icon: '😴', label: '睡饱饱', pct: 95, low: false, cls: 'bf-energy' }
    ],
    shopCat: 'hat',
    goods: [],
    diary: { days: 1, done: 0, total: 10, miles: [] },
    sign: { show: false, canClaim: false, btnText: '', days: [] },
    modal: { show: false, type: 'normal', emoji: '🎉', title: '', sub: '', ok: '好的', cancel: '', gift: '', lv: 1 },
    game: { show: false, over: false, score: 0, time: 20, final: 0, reward: '', bubbles: [] },
    toasts: [],
    confetti: []
  },

  /* ========== 存档 ========== */
  _load() {
    try {
      const raw = wx.getStorageSync(KEY);
      if (raw && typeof raw === 'object' && raw.born) {
        const s = Object.assign({}, DEF, raw);
        if (!Array.isArray(s.miles)) s.miles = [];
        if (!s.owned || typeof s.owned !== 'object') {
          s.owned = { hat: ['bow'], clothes: ['onesie'], room: ['peach'] };
        }
        ['hat', 'clothes', 'room'].forEach(k => {
          if (!Array.isArray(s.owned[k])) s.owned[k] = DEF.owned[k].slice();
        });
        return s;
      }
    } catch (e) {}
    return null;
  },
  _save() {
    this.S.lastTs = Date.now();
    try { wx.setStorageSync(KEY, this.S); } catch (e) {}
  },

  /* ========== 音效 / 震动 ========== */
  _beep(freq, dur, type, vol, slide) {
    if (!this.S || !this.S.sound) return;
    try {
      if (!this._ac) this._ac = wx.createWebAudioContext();
      const c = this._ac;
      const o = c.createOscillator();
      const g = c.createGain();
      o.type = type || 'sine';
      o.frequency.setValueAtTime(freq, c.currentTime);
      if (slide) o.frequency.exponentialRampToValueAtTime(slide, c.currentTime + dur);
      g.gain.setValueAtTime(vol || 0.16, c.currentTime);
      g.gain.exponentialRampToValueAtTime(0.001, c.currentTime + dur);
      o.connect(g);
      g.connect(c.destination);
      o.start();
      o.stop(c.currentTime + dur + 0.02);
    } catch (e) {}
  },
  _sfxTap() { this._beep(520, 0.08, 'triangle', 0.12, 700); },
  _sfxCare() {
    this._beep(660, 0.1, 'sine', 0.14, 990);
    setTimeout(() => this._beep(880, 0.12, 'sine', 0.12, 1180), 70);
  },
  _sfxCoin() { this._beep(1050, 0.07, 'square', 0.07, 1400); },
  _sfxPop() { this._beep(760, 0.06, 'sine', 0.12, 1200); },
  _sfxLevel() { [523, 659, 784, 1046].forEach((f, i) => setTimeout(() => this._beep(f, 0.16, 'triangle', 0.14), i * 90)); },
  _sfxSign() { [660, 880, 1100].forEach((f, i) => setTimeout(() => this._beep(f, 0.14, 'sine', 0.13), i * 80)); },
  _sfxErr() { this._beep(220, 0.12, 'square', 0.08, 160); },
  _sfxSleep() { this._beep(440, 0.3, 'sine', 0.1, 220); },
  _buzz() {
    try { wx.vibrateShort({ type: 'light' }); } catch (e) {}
  },
  _buzzMid() {
    try { wx.vibrateShort({ type: 'medium' }); } catch (e) {}
  },

  /* ========== 状态计算 ========== */
  _avgStat() { return (this.S.hunger + this.S.joy + this.S.clean + this.S.energy) / 4; },
  _anyLow() {
    const S = this.S;
    return S.hunger < 25 || S.joy < 25 || S.clean < 25 || S.energy < 25;
  },
  _mood() {
    if (this.S.sleeping) return 'sleep';
    if (this._anyLow()) return 'cry';
    if (this._avgStat() >= 85) return 'happy';
    return 'normal';
  },
  _applyDecay(mins) {
    mins = clamp(mins, 0, 60 * 14);
    if (mins <= 0) return;
    const S = this.S;
    const sleeping = !!S.sleeping;
    for (const k in DECAY) {
      let d = DECAY[k] * mins * (sleeping && k !== 'energy' ? 0.35 : 1);
      if (sleeping && k === 'energy') d = -5 * mins;
      S[k] = clamp(S[k] - d, 0, 100);
    }
  },

  /* ========== 同步渲染 ========== */
  _syncTop() {
    const S = this.S;
    const st = STAGES[stageOf(S.level)];
    this.setData({
      'top.name': S.name || '宝宝',
      'top.face': st.e,
      'top.lvTag': 'LV.' + S.level + ' ' + st.n,
      'top.xpPct': Math.round(clamp(S.exp / expNeed(S.level) * 100, 0, 100)),
      'top.coins': Math.floor(S.coins),
      'top.soundIcon': S.sound ? '🔊' : '🔇'
    });
  },
  _syncBars() {
    const S = this.S;
    const keys = ['hunger', 'joy', 'clean', 'energy'];
    const bars = this.data.bars.map((b, i) => {
      const pct = Math.round(S[keys[i]]);
      return Object.assign({}, b, { pct: pct, low: pct < 25 });
    });
    this.setData({ bars: bars });
  },
  _syncRoom() {
    const S = this.S;
    this.setData({
      'room.mood': this._mood(),
      'room.stage': stageOf(S.level),
      'room.hat': S.owned.hat.indexOf(S.hat) >= 0 ? S.hat : 'bow',
      'room.clothes': S.clothes,
      'room.wall': S.room,
      'room.plant': S.room === 'forest' ? '🌳' : '🌱'
    });
  },
  _syncAll() { this._syncTop(); this._syncBars(); this._syncRoom(); },

  /* ========== toast / 弹窗 ========== */
  _toast(text, gold) {
    const id = Date.now() + '_' + Math.random();
    const list = this.data.toasts.concat([{ id: id, text: text, gold: !!gold }]);
    this.setData({ toasts: list });
    setTimeout(() => {
      this.setData({ toasts: this.data.toasts.filter(t => t.id !== id) });
    }, 1900);
  },
  _modal(o) {
    this.setData({
      modal: {
        show: true,
        type: o.type || 'normal',
        emoji: o.emoji || '🎉',
        title: o.title || '',
        sub: o.sub || '',
        ok: o.ok || '好的',
        cancel: o.cancel || '',
        gift: o.gift || '',
        lv: o.lv || 1
      }
    });
  },
  onModalOk() {
    this.setData({ 'modal.show': false });
    this._sfxTap();
    if (this._mdOk) { const f = this._mdOk; this._mdOk = null; f(); }
  },
  onModalCancel() {
    this.setData({ 'modal.show': false });
    this._sfxTap();
  },

  /* ========== 里程碑 / 成长 ========== */
  _unlock(id) {
    if (this.S.miles.indexOf(id) >= 0) return;
    const m = MILES.find(x => x.id === id);
    if (!m) return;
    this.S.miles.push(id);
    this._save();
    this._toast('📖 日记更新：' + m.n, true);
    this._sfxLevel();
    this._syncDiary();
  },
  _checkMiles() {
    const S = this.S;
    if (S.feed >= 1) this._unlock('feed');
    if (S.bath >= 1) this._unlock('bath');
    if (S.sleeps >= 1) this._unlock('sleep');
    if (S.joy >= 99) this._unlock('happy');
    if (S.level >= 3) this._unlock('lv3');
    if (S.level >= 6) this._unlock('lv6');
    if (S.streak >= 3) this._unlock('sign3');
    if (S.games >= 5) this._unlock('game5');
    if (S.coins >= 1000) this._unlock('rich');
    if (S.hunger >= 80 && S.joy >= 80 && S.clean >= 80 && S.energy >= 80) this._unlock('all1');
  },
  _addExp(n) {
    this.S.exp += n;
    let leveled = false;
    while (this.S.exp >= expNeed(this.S.level)) {
      this.S.exp -= expNeed(this.S.level);
      this.S.level += 1;
      leveled = true;
    }
    if (leveled) {
      this._save();
      this._syncTop();
      this._syncRoom();
      this._sfxLevel();
      this._buzzMid();
      this._confetti();
      const st = STAGES[stageOf(this.S.level)];
      this._modal({
        type: 'levelup',
        lv: this.S.level,
        title: '宝宝升级啦！',
        sub: 'LV.' + this.S.level + ' · ' + st.n + '\n继续好好照顾它，看看长大的样子！',
        ok: '太棒了'
      });
      this._checkMiles();
    }
    this._syncTop();
  },
  _addCoins(n) {
    this.S.coins += n;
    this._save();
    this._syncTop();
    this._checkMiles();
  },
  _confetti() {
    const colors = ['#FF8FA3', '#7EC8E3', '#FFC94A', '#7ED9A7', '#C6B5F0'];
    const list = [];
    for (let i = 0; i < 36; i++) {
      list.push({
        id: i + '_' + Date.now(),
        left: Math.round(rnd(0, 100)),
        color: colors[i % colors.length],
        dur: rnd(1.4, 2.6).toFixed(2),
        delay: rnd(0, 0.4).toFixed(2)
      });
    }
    this.setData({ confetti: list });
    setTimeout(() => this.setData({ confetti: [] }), 3600);
  },
  /* ========== 引导 ========== */
  pickGirl() { this.setData({ obGender: 'girl' }); this._sfxTap(); },
  pickBoy() { this.setData({ obGender: 'boy' }); this._sfxTap(); },
  onNameInput(e) { this.setData({ obName: e.detail.value }); },
  onStart() {
    const nm = (this.data.obName || '').trim() ||
      (this.data.obGender === 'girl' ? '小棉袄' : '小暖阳');
    this.S = Object.assign({}, DEF, {
      name: nm,
      gender: this.data.obGender,
      born: Date.now(),
      lastTs: Date.now(),
      owned: { hat: ['bow'], clothes: ['onesie'], room: ['peach'] },
      miles: []
    });
    this._save();
    this._sfxCare();
    this._buzz();
    this.setData({ started: true });
    this._syncAll();
    this._syncGoods();
    this._syncDiary();
    this._checkDaily(false);
  },

  /* ========== 照顾动作 ========== */
  _floatFx(txt) {
    const id = Date.now();
    this.setData({ 'room.fxShow': true, 'room.fxText': txt, 'room.fxId': id });
    clearTimeout(this._fxT);
    this._fxT = setTimeout(() => this.setData({ 'room.fxShow': false }), 900);
  },
  _thought(t) {
    this.setData({ 'room.thoughtShow': true, 'room.thoughtText': t });
    clearTimeout(this._thT);
    this._thT = setTimeout(() => this.setData({ 'room.thoughtShow': false }), 2600);
  },
  _wakeIfSleeping() {
    if (!this.S.sleeping) return false;
    this.S.sleeping = false;
    this._floatFx('☀️');
    this._thought('睡醒啦！');
    this._syncRoom();
    return true;
  },
  _weakest() {
    const S = this.S;
    const ks = ['hunger', 'joy', 'clean', 'energy'];
    ks.sort((a, b) => S[a] - S[b]);
    return ks[0];
  },
  onCare(e) {
    const kind = e.currentTarget.dataset.kind;
    const now = Date.now();
    if (now < (this._busy || 0)) return;
    if (kind === 'sleep') {
      this.S.sleeping = !this.S.sleeping;
      if (this.S.sleeping) {
        this.S.sleeps++;
        this._floatFx('💤');
        this._thought('晚安……');
        this._sfxSleep();
        this._buzz();
        this._toast('宝宝睡着了，体力恢复中');
        this._addExp(4);
      } else {
        this._floatFx('☀️');
        this._thought('睡醒啦！');
        this._sfxTap();
        this._toast('宝宝醒来了');
      }
      this._sfxCare();
      this._buzz();
      this._checkMiles();
      this._save();
      this._syncAll();
      return;
    }
    if (this._wakeIfSleeping()) this._toast('宝宝醒过来啦');
    if (kind === 'play') { this._openGame(); return; }
    this._busy = now + 450;
    const S = this.S;
    if (kind === 'feed') {
      S.hunger = clamp(S.hunger + 34, 0, 100);
      S.feed++;
      S.clean = clamp(S.clean - 4, 0, 100);
      this._floatFx('🍼+34');
      this._thought('咕噜咕噜~真好喝！');
      this._addCoins(3);
      this._addExp(6);
      if (S.hunger >= 99) this._toast('宝宝吃饱啦 +6 经验');
    } else if (kind === 'bath') {
      S.clean = clamp(S.clean + 42, 0, 100);
      S.bath++;
      S.joy = clamp(S.joy + 6, 0, 100);
      this._floatFx('🛁+42');
      this._thought('哗啦哗啦~洗得香香！');
      this._addCoins(3);
      this._addExp(6);
    }
    S.joy = clamp(S.joy + 2, 0, 100);
    this._sfxCare();
    this._buzz();
    this._checkMiles();
    this._save();
    this._syncAll();
  },
  onBabyTap() {
    if (this.S.sleeping) { this._thought('嘘……在做梦'); return; }
    this.S.joy = clamp(this.S.joy + 5, 0, 100);
    const fx = ['💗', '✨', '😜', '🥰'][Math.floor(Math.random() * 4)];
    const lines = ['咯咯咯~', '妈妈最好啦', '还要玩！', '么么哒！'];
    this._floatFx(fx);
    this._thought(lines[Math.floor(Math.random() * lines.length)]);
    this._sfxTap();
    this._buzz();
    this._save();
    this._syncRoom();
    this._syncBars();
  },

  /* ========== 页签 ========== */
  onTab(e) {
    const v = e.currentTarget.dataset.v;
    if (v === this.data.tab) return;
    this.setData({ tab: v });
    this._sfxTap();
    if (v === 'shop') this._syncGoods();
    if (v === 'diary') this._syncDiary();
  },

  /* ========== 装扮商店 ========== */
  onShopCat(e) {
    this.setData({ shopCat: e.currentTarget.dataset.cat });
    this._sfxTap();
    this._syncGoods();
  },
  _syncGoods() {
    const cat = this.data.shopCat;
    const S = this.S;
    const clsMap = { hat: '', clothes: 'b', room: 'c' };
    const list = GOODS[cat].map(g => {
      const owned = S.owned[cat].indexOf(g.id) >= 0;
      return {
        id: g.id, n: g.n, e: g.e, p: g.p,
        owned: owned, eq: S[cat] === g.id,
        cls: clsMap[cat]
      };
    });
    this.setData({ goods: list });
  },
  onBuy(e) {
    const id = e.currentTarget.dataset.id;
    const cat = this.data.shopCat;
    const g = GOODS[cat].find(x => x.id === id);
    if (!g) return;
    if (this.S.coins < g.p) {
      this._sfxErr();
      this._toast('金币不够，去陪玩赚点吧');
      return;
    }
    this.S.coins -= g.p;
    if (this.S.owned[cat].indexOf(id) < 0) this.S.owned[cat].push(id);
    this.S[cat] = id;
    this._sfxCoin();
    this._buzz();
    this._toast('购入成功：' + g.n, true);
    this._save();
    this._syncGoods();
    this._syncTop();
    this._syncRoom();
  },
  onEquip(e) {
    const id = e.currentTarget.dataset.id;
    const cat = this.data.shopCat;
    this.S[cat] = id;
    this._sfxTap();
    this._save();
    this._syncGoods();
    this._syncRoom();
  },

  /* ========== 日记 ========== */
  _syncDiary() {
    const S = this.S;
    const days = Math.max(1, Math.ceil((Date.now() - (S.born || Date.now())) / 86400000));
    const miles = MILES.map(m => ({
      id: m.id, e: m.e, n: m.n, d: m.d,
      ok: S.miles.indexOf(m.id) >= 0
    }));
    this.setData({
      diary: {
        days: days,
        done: S.miles.length,
        total: MILES.length,
        miles: miles
      }
    });
  },
  /* ========== 每日签到 ========== */
  _signIdx() {
    const S = this.S;
    const t = todayStr();
    if (S.lastSign === t) return clamp((S.streak || 1) - 1, 0, 6);
    if (S.lastSign === yesterdayStr()) return clamp(S.streak, 0, 6);
    return 0;
  },
  _syncSign() {
    const S = this.S;
    const signed = S.lastSign === todayStr();
    const idx = this._signIdx();
    const days = [];
    for (let i = 0; i < 7; i++) {
      let state = 'todo';
      if (signed ? i <= idx : i < idx) state = 'done';
      else if (i === idx && !signed) state = 'today';
      days.push({ i: i + 1, g: SIGN_REWARDS[i], state: state });
    }
    this.setData({
      sign: {
        show: this.data.sign.show,
        canClaim: !signed,
        btnText: signed
          ? '今天已领取 ✅'
          : '领取今日奖励（第 ' + (idx + 1) + ' 天 · ¥' + SIGN_REWARDS[idx] + '）',
        days: days
      }
    });
  },
  _openSign() {
    this._syncSign();
    this.setData({ 'sign.show': true });
  },
  onSignBtn() { this._openSign(); this._sfxTap(); },
  onSignClose() { this.setData({ 'sign.show': false }); this._sfxTap(); },
  onSignClaim() {
    const S = this.S;
    const t = todayStr();
    if (S.lastSign === t) return;
    const idx = this._signIdx();
    S.streak = idx + 1;
    S.lastSign = t;
    const rw = SIGN_REWARDS[idx];
    S.coins += rw;
    S.signIn++;
    this._sfxSign();
    this._buzzMid();
    this._save();
    this._syncTop();
    this._checkMiles();
    this._modal({
      emoji: '🎁',
      title: '签到成功！',
      sub: '连续第 ' + S.streak + ' 天签到\n获得 ' + rw + ' 金币' +
        (S.streak >= 7 ? '\n7 天全勤达成，明天奖励循环重置！' : ''),
      ok: '收下'
    });
    this._syncSign();
  },
  _checkDaily(silent) {
    const S = this.S;
    const t = todayStr();
    if (S.lastSign && S.lastSign !== t && S.lastSign !== yesterdayStr()) {
      S.streak = 0;
      this._save();
    }
    if (S.lastSign !== t && !silent) {
      setTimeout(() => this._openSign(), 700);
    }
  },
  _welcomeBack() {
    const now = Date.now();
    const mins = (now - (this.S.lastTs || now)) / 60000;
    if (mins < 45) return;
    const h = Math.floor(mins / 60);
    const m = Math.floor(mins % 60);
    const gift = clamp(Math.floor(mins / 30) * 5, 5, 80);
    this.S.coins += gift;
    this._save();
    this._syncTop();
    const timeTxt = h > 0 ? h + ' 小时 ' + m + ' 分钟' : Math.floor(mins) + ' 分钟';
    this._modal({
      emoji: '🏠',
      title: '你回来啦！',
      sub: '你离开了 ' + timeTxt + '\n宝宝一直乖乖等你，饿得肚子咕咕叫～\n想你想到哭唧唧',
      gift: '🎁 回归小礼物 +' + gift + ' 金币',
      ok: '抱抱宝宝'
    });
  },

  /* ========== 泡泡小游戏 ========== */
  _openGame() {
    this.setData({ 'game.show': true, 'game.over': false });
    this._startGame();
    this._sfxTap();
  },
  _startGame() {
    this._clearGameTimers();
    this.setData({
      'game.score': 0,
      'game.time': 20,
      'game.over': false,
      'game.final': 0,
      'game.reward': '',
      'game.bubbles': []
    });
    this._gScore = 0;
    this._gTime = 20;
    this._gRunning = true;
    this._gId = 0;
    this._spawnT = setInterval(() => this._spawnBubble(), 420);
    this._gameT = setInterval(() => {
      if (!this._gRunning) return;
      this._gTime--;
      this.setData({ 'game.time': this._gTime });
      if (this._gTime <= 0) this._endGame();
    }, 1000);
  },
  _spawnBubble() {
    if (!this._gRunning) return;
    const colors = ['#FF8FA3', '#7EC8E3', '#FFC94A', '#7ED9A7', '#C6B5F0'];
    const emo = ['⭐', '🍬', '🍓', '🌈', '🔵'];
    const size = Math.round(rnd(104, 184));
    const color = colors[Math.floor(Math.random() * colors.length)];
    const dur = rnd(3.4, 5.2);
    const b = {
      id: 'b' + (++this._gId),
      left: Math.round(rnd(6, 86)),
      size: size,
      dur: dur.toFixed(2),
      e: emo[Math.floor(Math.random() * emo.length)],
      bg: 'radial-gradient(circle at 32% 28%, rgba(255,255,255,.95), ' + color + ' 58%)',
      popped: false
    };
    const list = this.data.game.bubbles.concat([b]);
    this.setData({ 'game.bubbles': list });
    setTimeout(() => this._removeBubble(b.id), dur * 1000 + 800);
  },
  _removeBubble(id) {
    const list = this.data.game.bubbles.filter(b => b.id !== id);
    if (list.length !== this.data.game.bubbles.length) {
      this.setData({ 'game.bubbles': list });
    }
  },
  onPopBubble(e) {
    if (!this._gRunning) return;
    const id = e.currentTarget.dataset.id;
    const b = this.data.game.bubbles.find(x => x.id === id);
    if (!b || b.popped) return;
    this._gScore++;
    this.setData({
      'game.score': this._gScore,
      'game.bubbles': this.data.game.bubbles.map(x =>
        x.id === id ? Object.assign({}, x, { popped: true }) : x)
    });
    this._sfxPop();
    this._buzz();
    setTimeout(() => this._removeBubble(id), 220);
  },
  _endGame() {
    this._gRunning = false;
    this._clearGameTimers();
    const rw = this._gScore;
    this.S.games++;
    this.S.joy = clamp(this.S.joy + 18, 0, 100);
    this._addCoins(rw);
    this._addExp(8 + Math.floor(rw / 3));
    this._save();
    this._checkMiles();
    this._syncAll();
    this.setData({
      'game.over': true,
      'game.final': rw,
      'game.reward': '获得 ' + rw + ' 金币 + 心情大涨！'
    });
    if (rw >= 15) this._sfxLevel(); else this._sfxCoin();
  },
  _clearGameTimers() {
    if (this._spawnT) { clearInterval(this._spawnT); this._spawnT = null; }
    if (this._gameT) { clearInterval(this._gameT); this._gameT = null; }
  },
  onGameClose() {
    this._gRunning = false;
    this._clearGameTimers();
    this.setData({ 'game.show': false, 'game.bubbles': [] });
    this._sfxTap();
    this._syncAll();
  },
  onGameBack() {
    this.setData({ 'game.show': false, 'game.bubbles': [] });
    this._sfxTap();
    this._syncAll();
  },
  onGameAgain() {
    this._startGame();
    this._sfxTap();
  },
  onSoundToggle() {
    this.S.sound = !this.S.sound;
    this._save();
    this._syncTop();
    if (this.S.sound) this._sfxTap();
    this._toast(this.S.sound ? '音效已开启' : '音效已关闭');
  },

  /* ========== 生命周期 ========== */
  onLoad() {
    const saved = this._load();
    if (saved) {
      this.S = saved;
      this.setData({ started: true });
      this._applyDecay((Date.now() - (saved.lastTs || Date.now())) / 60000);
      if (saved.lastSign && saved.lastSign !== todayStr() && saved.lastSign !== yesterdayStr()) {
        this.S.streak = 0;
      }
      this._save();
      this._syncAll();
      this._syncGoods();
      this._syncDiary();
      this._checkDaily(false);
      this._welcomeBack();
      this._decayT = setInterval(() => this._tick(), 2000);
      this._lastTick = Date.now();
    } else {
      this.S = Object.assign({}, DEF, {
        owned: { hat: ['bow'], clothes: ['onesie'], room: ['peach'] },
        miles: []
      });
      this.setData({ started: false });
      this._syncGoods();
      this._syncDiary();
    }
  },
  onShow() {
    if (!this.S || !this.S.born) return;
    const now = Date.now();
    if (this._lastTick) {
      this._applyDecay((now - this._lastTick) / 60000);
      this._syncBars();
      this._syncRoom();
    }
    this._lastTick = now;
  },
  onHide() {
    if (this.S && this.S.born) this._save();
  },
  onUnload() {
    if (this._decayT) clearInterval(this._decayT);
    this._clearGameTimers();
    clearTimeout(this._fxT);
    clearTimeout(this._thT);
    if (this.S && this.S.born) this._save();
  },
  _tick() {
    if (!this.S || !this.S.born) return;
    const now = Date.now();
    const mins = (now - (this._lastTick || now)) / 60000;
    this._lastTick = now;
    this._applyDecay(mins);
    this._syncBars();
    this._syncRoom();
    if (Math.random() < 0.03 && !this.S.sleeping) {
      const w = this._weakest();
      if (this.S[w] < 30) this._thought(NEED[w] + '……');
    }
    if (Math.floor(now / 10000) !== Math.floor((now - 2000) / 10000)) this._save();
  }
});

