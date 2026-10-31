// Word -> emoji for the Icons style. A starter set; grow it over time (the plan targets ~800 words).
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else root.LyricalIcons = factory();
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';
  const MAP = {
    love: '❤️', heart: '💗', fire: '🔥', burn: '🔥', flame: '🔥', sun: '☀️', sunshine: '☀️', moon: '🌙', night: '🌙',
    star: '⭐', sky: '🌌', cloud: '☁️', rain: '🌧️', storm: '⛈️', thunder: '⚡', lightning: '⚡', snow: '❄️', cold: '🥶',
    wind: '💨', sea: '🌊', ocean: '🌊', wave: '🌊', water: '💧', river: '🏞️', mountain: '⛰️', island: '🏝️', beach: '🏖️',
    flower: '🌸', rose: '🌹', tree: '🌳', forest: '🌲', leaf: '🍃', garden: '🌷',
    bird: '🐦', dove: '🕊️', fly: '🕊️', wing: '🪽', dog: '🐶', cat: '🐱', horse: '🐴', wolf: '🐺', fish: '🐟', butterfly: '🦋',
    song: '🎵', music: '🎶', sing: '🎤', voice: '🎤', guitar: '🎸', piano: '🎹', drum: '🥁', radio: '📻', dance: '💃', party: '🎉',
    kiss: '💋', smile: '😊', laugh: '😂', cry: '😢', tear: '😢', eye: '👀', hand: '✋', finger: '☝️', face: '🙂', mind: '🧠', brain: '🧠',
    home: '🏠', house: '🏠', door: '🚪', window: '🪟', room: '🛋️', bed: '🛏️', sleep: '😴', dream: '💭', city: '🏙️', street: '🛣️',
    road: '🛣️', car: '🚗', train: '🚆', plane: '✈️', ship: '🚢', boat: '⛵', bike: '🚲', run: '🏃', walk: '🚶', drive: '🚗',
    time: '⏰', clock: '⏰', watch: '⌚', phone: '📱', call: '📞', light: '✨', shine: '✨', glow: '✨', spark: '✨', magic: '🪄',
    gold: '🥇', money: '💰', diamond: '💎', crown: '👑', king: '👑', queen: '👸', ghost: '👻', devil: '😈', angel: '😇', skull: '💀',
    death: '💀', dead: '💀', blood: '🩸', gun: '🔫', sword: '⚔️', key: '🔑', lock: '🔒', chain: '⛓️', bell: '🔔',
    coffee: '☕', wine: '🍷', drink: '🍸', beer: '🍺', pizza: '🍕', cake: '🍰', candy: '🍬', apple: '🍎', cherry: '🍒',
    baby: '👶', girl: '👧', boy: '👦', mother: '👩', father: '👨', friend: '🤝', world: '🌍', earth: '🌍', moonlight: '🌙',
    paper: '📄', book: '📖', letter: '✉️', write: '✍️', mirror: '🪞', camera: '📷', film: '🎬', game: '🎮', rocket: '🚀',
    summer: '🌞', winter: '⛄', autumn: '🍂', spring: '🌱', morning: '🌅', sunrise: '🌅', sunset: '🌇', rainbow: '🌈', kettle: '🫖'
  };

  function clean(word) { return String(word || '').toLowerCase().replace(/[’']s$/, '').replace(/[^a-z]/g, ''); }

  // Looks up a word, trying simple endings: stars, kissed, dancing -> dance.
  function iconFor(word) {
    const w = clean(word);
    if (w.length < 3) return null;
    const tries = [w];
    if (w.endsWith('ies')) tries.push(w.slice(0, -3) + 'y');
    if (w.endsWith('es')) tries.push(w.slice(0, -2));
    if (w.endsWith('s')) tries.push(w.slice(0, -1));
    if (w.endsWith('ing')) { const b = w.slice(0, -3); tries.push(b, b + 'e'); if (b.length > 2 && b[b.length - 1] === b[b.length - 2]) tries.push(b.slice(0, -1)); }
    if (w.endsWith('ed')) { const b = w.slice(0, -2); tries.push(b, b + 'e', w.slice(0, -1)); }
    for (const t of tries) if (Object.prototype.hasOwnProperty.call(MAP, t)) return MAP[t];
    return null;
  }

  return { iconFor, size: Object.keys(MAP).length };
});
