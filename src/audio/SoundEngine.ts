/**
 * SoundEngine: Процедурный аудиодвижок на базе Web Audio API.
 * Генерирует звуки шагов, скрипа половиц, мурлыканья кота, храпа,
 * хоррор-стингеров и рождественских колокольчиков без внешних MP3-файлов.
 */
export class SoundEngine {
  private ctx: AudioContext | null = null;
  private isMuted: boolean = false;
  private musicInterval: number | null = null;

  constructor() {
    // AudioContext инициализируется по первому клику пользователя
  }

  public init() {
    if (!this.ctx) {
      const AudioCtx = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
      this.ctx = new AudioCtx();
    }
    if (this.ctx.state === 'suspended') {
      this.ctx.resume();
    }
    this.startAmbientMusic();
  }

  // --- ШАГИ ---
  public playFootstep(type: 'snow' | 'wood' | 'carpet' | 'crouch') {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;

    if (type === 'snow') {
      // Хруст снега: белый шум через полосовой фильтр с быстрым затуханием
      this.playNoise(0.08, 400, 1800, 0.15);
    } else if (type === 'wood') {
      // Шаг по доске: короткий щелчок + низкий резонанс
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();
      osc.type = 'triangle';
      osc.frequency.setValueAtTime(140 + Math.random() * 20, t);
      osc.frequency.exponentialRampToValueAtTime(40, t + 0.08);

      gain.gain.setValueAtTime(0.25, t);
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.08);

      osc.connect(gain);
      gain.connect(this.ctx.destination);
      osc.start(t);
      osc.stop(t + 0.09);
    } else if (type === 'carpet' || type === 'crouch') {
      // Мягкий тихий шаг на цыпочках
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(80, t);
      osc.frequency.exponentialRampToValueAtTime(30, t + 0.05);

      gain.gain.setValueAtTime(0.08, t);
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.05);

      osc.connect(gain);
      gain.connect(this.ctx.destination);
      osc.start(t);
      osc.stop(t + 0.06);
    }
  }

  // --- СКРИП ПАРКЕТА ---
  public playCreak() {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;

    const osc = this.ctx.createOscillator();
    const lfo = this.ctx.createOscillator();
    const lfoGain = this.ctx.createGain();
    const filter = this.ctx.createBiquadFilter();
    const gain = this.ctx.createGain();

    osc.type = 'sawtooth';
    const baseFreq = 220 + Math.random() * 120;
    osc.frequency.setValueAtTime(baseFreq, t);
    osc.frequency.linearRampToValueAtTime(baseFreq * (0.8 + Math.random() * 0.4), t + 0.35);

    // LFO для дрожания скрипа
    lfo.type = 'triangle';
    lfo.frequency.setValueAtTime(25 + Math.random() * 10, t);
    lfoGain.gain.setValueAtTime(35, t);
    lfo.connect(osc.frequency);

    filter.type = 'bandpass';
    filter.frequency.setValueAtTime(baseFreq * 2.5, t);
    filter.Q.setValueAtTime(4.0, t);

    gain.gain.setValueAtTime(0.3, t);
    gain.gain.linearRampToValueAtTime(0.35, t + 0.1);
    gain.gain.exponentialRampToValueAtTime(0.001, t + 0.35);

    osc.connect(filter);
    filter.connect(gain);
    gain.connect(this.ctx.destination);

    lfo.start(t);
    osc.start(t);
    lfo.stop(t + 0.36);
    osc.stop(t + 0.36);
  }

  // --- СКРИП ДВЕРИ ---
  public playDoorCreak() {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;

    const osc = this.ctx.createOscillator();
    const gain = this.ctx.createGain();
    const filter = this.ctx.createBiquadFilter();

    osc.type = 'sawtooth';
    osc.frequency.setValueAtTime(300, t);
    osc.frequency.linearRampToValueAtTime(450, t + 0.3);
    osc.frequency.linearRampToValueAtTime(280, t + 0.6);

    filter.type = 'lowpass';
    filter.frequency.setValueAtTime(900, t);

    gain.gain.setValueAtTime(0.2, t);
    gain.gain.exponentialRampToValueAtTime(0.001, t + 0.65);

    osc.connect(filter);
    filter.connect(gain);
    gain.connect(this.ctx.destination);

    osc.start(t);
    osc.stop(t + 0.7);
  }

  // --- УДАР / ПАДЕНИЕ ПРЕДМЕТА ---
  public playThud(loudness: number = 0.5) {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;

    const osc = this.ctx.createOscillator();
    const gain = this.ctx.createGain();
    osc.type = 'triangle';
    osc.frequency.setValueAtTime(120, t);
    osc.frequency.exponentialRampToValueAtTime(25, t + 0.25);

    gain.gain.setValueAtTime(Math.min(1.0, loudness), t);
    gain.gain.exponentialRampToValueAtTime(0.001, t + 0.25);

    osc.connect(gain);
    gain.connect(this.ctx.destination);
    osc.start(t);
    osc.stop(t + 0.26);

    // Дополнительный шум удара
    this.playNoise(0.08, 100, 600, loudness * 0.4);
  }

  // --- ЗВОН РАЗБИТОЙ / ЗАДЕТОЙ ЧАШКИ ---
  public playClatter() {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;
    [1200, 1850, 2400].forEach((freq) => {
      if (!this.ctx) return;
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(freq + (Math.random() - 0.5) * 50, t);
      gain.gain.setValueAtTime(0.12, t);
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.4);
      osc.connect(gain);
      gain.connect(this.ctx.destination);
      osc.start(t);
      osc.stop(t + 0.45);
    });
  }

  // --- СВИСТ ПОЛЕТА В ДЫМОХОДЕ ---
  public playChimneyWhoosh() {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;
    this.playNoise(0.7, 100, 800, 0.4);

    const osc = this.ctx.createOscillator();
    const gain = this.ctx.createGain();
    osc.type = 'sine';
    osc.frequency.setValueAtTime(400, t);
    osc.frequency.exponentialRampToValueAtTime(90, t + 0.6);
    gain.gain.setValueAtTime(0.2, t);
    gain.gain.exponentialRampToValueAtTime(0.001, t + 0.6);
    osc.connect(gain);
    gain.connect(this.ctx.destination);
    osc.start(t);
    osc.stop(t + 0.65);
  }

  // --- МЯУКАНЬЕ КОТА ---
  public playCatMeow() {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;
    const osc = this.ctx.createOscillator();
    const gain = this.ctx.createGain();
    osc.type = 'triangle';

    // Форма мяу: подъем и спад частоты
    osc.frequency.setValueAtTime(520, t);
    osc.frequency.linearRampToValueAtTime(780, t + 0.15);
    osc.frequency.linearRampToValueAtTime(440, t + 0.5);

    gain.gain.setValueAtTime(0.01, t);
    gain.gain.linearRampToValueAtTime(0.2, t + 0.15);
    gain.gain.exponentialRampToValueAtTime(0.001, t + 0.5);

    osc.connect(gain);
    gain.connect(this.ctx.destination);
    osc.start(t);
    osc.stop(t + 0.52);
  }

  // --- ХРАП ЖИЛЬЦА ---
  public playSnore() {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;
    const osc = this.ctx.createOscillator();
    const gain = this.ctx.createGain();
    const lfo = this.ctx.createOscillator();
    const lfoGain = this.ctx.createGain();

    osc.type = 'sawtooth';
    osc.frequency.setValueAtTime(65, t);

    lfo.type = 'sine';
    lfo.frequency.setValueAtTime(9, t);
    lfoGain.gain.setValueAtTime(15, t);
    lfo.connect(osc.frequency);

    gain.gain.setValueAtTime(0.01, t);
    gain.gain.linearRampToValueAtTime(0.12, t + 0.4);
    gain.gain.linearRampToValueAtTime(0.001, t + 0.9);

    osc.connect(gain);
    gain.connect(this.ctx.destination);

    lfo.start(t);
    osc.start(t);
    lfo.stop(t + 0.95);
    osc.stop(t + 0.95);
  }

  // --- ХОРРОР СТИНГЕР (Жуткий аккорд при пасхалке / тревоге) ---
  public playHorrorStinger() {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;

    // Диссонирующие тритоны (E, Bb, Eb, A)
    const notes = [164.81, 233.08, 311.13, 440.00, 622.25];
    notes.forEach((freq) => {
      if (!this.ctx) return;
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();
      osc.type = 'sawtooth';
      osc.frequency.setValueAtTime(freq, t);
      osc.frequency.linearRampToValueAtTime(freq * 0.96, t + 1.2);

      gain.gain.setValueAtTime(0.18, t);
      gain.gain.exponentialRampToValueAtTime(0.001, t + 1.2);

      const filter = this.ctx.createBiquadFilter();
      filter.type = 'lowpass';
      filter.frequency.setValueAtTime(900, t);

      osc.connect(filter);
      filter.connect(gain);
      gain.connect(this.ctx.destination);

      osc.start(t);
      osc.stop(t + 1.25);
    });
  }

  // --- РОЖДЕСТВЕНСКИЙ КОЛОКОЛЬЧИК (Jingle Bell) ---
  public playJingleBell() {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;
    const freqs = [1800, 2400, 3100];
    freqs.forEach((f) => {
      if (!this.ctx) return;
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(f, t);
      gain.gain.setValueAtTime(0.08, t);
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.35);
      osc.connect(gain);
      gain.connect(this.ctx.destination);
      osc.start(t);
      osc.stop(t + 0.4);
    });
  }

  // --- ФОНОВЫЙ ЗИМНИЙ ЭМБИЕНТ И КОЛОКОЛЬЧИКИ ---
  private startAmbientMusic() {
    if (this.musicInterval) clearInterval(this.musicInterval);

    // Периодический легкий рождественский перезвон
    const melody = [523.25, 587.33, 659.25, 523.25, 659.25, 783.99]; // C E G мотивы
    let idx = 0;
    this.musicInterval = window.setInterval(() => {
      if (!this.ctx || this.isMuted) return;
      const t = this.ctx.currentTime;
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(melody[idx % melody.length], t);
      gain.gain.setValueAtTime(0.02, t);
      gain.gain.exponentialRampToValueAtTime(0.0001, t + 0.9);
      osc.connect(gain);
      gain.connect(this.ctx.destination);
      osc.start(t);
      osc.stop(t + 0.95);
      idx++;
    }, 1800);
  }

  // Вспомогательный генератор фильтрованного шума
  private playNoise(duration: number, lowFreq: number, highFreq: number, volume: number) {
    if (!this.ctx) return;
    const bufferSize = this.ctx.sampleRate * duration;
    const buffer = this.ctx.createBuffer(1, bufferSize, this.ctx.sampleRate);
    const output = buffer.getChannelData(0);
    for (let i = 0; i < bufferSize; i++) {
      output[i] = Math.random() * 2 - 1;
    }

    const whiteNoise = this.ctx.createBufferSource();
    whiteNoise.buffer = buffer;

    const filter = this.ctx.createBiquadFilter();
    filter.type = 'bandpass';
    filter.frequency.setValueAtTime((lowFreq + highFreq) / 2, this.ctx.currentTime);
    filter.Q.setValueAtTime(1.2, this.ctx.currentTime);

    const gain = this.ctx.createGain();
    gain.gain.setValueAtTime(volume, this.ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.001, this.ctx.currentTime + duration);

    whiteNoise.connect(filter);
    filter.connect(gain);
    gain.connect(this.ctx.destination);

    whiteNoise.start();
  }
}
