import * as THREE from 'three';

export type FaceExpression = 'IDLE' | 'STRAIN' | 'SHOCK' | 'SNEAK';

/**
 * Генерирует 2D текстуру лица в стиле игры PEAK:
 * - румяные щечки
 * - завиток на лбу
 * - эмоции: обычная (o_o), натуга (>_< со стиснутыми зубами), испуг (O_O), крадущийся (-_-)
 */
export class FaceController {
  public texture: THREE.CanvasTexture;
  private canvas: HTMLCanvasElement;
  private ctx: CanvasRenderingContext2D;
  private currentExpression: FaceExpression = 'IDLE';

  constructor() {
    this.canvas = document.createElement('canvas');
    this.canvas.width = 256;
    this.canvas.height = 256;
    this.ctx = this.canvas.getContext('2d')!;
    this.texture = new THREE.CanvasTexture(this.canvas);
    this.drawFace('IDLE');
  }

  public setExpression(expr: FaceExpression) {
    if (this.currentExpression === expr) return;
    this.currentExpression = expr;
    this.drawFace(expr);
  }

  private drawFace(expr: FaceExpression) {
    const ctx = this.ctx;
    ctx.clearRect(0, 0, 256, 256);

    // Румяные розовые щечки (как на постере PEAK)
    ctx.fillStyle = 'rgba(255, 105, 135, 0.78)';
    ctx.beginPath();
    ctx.ellipse(62, 152, 20, 11, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.beginPath();
    ctx.ellipse(194, 152, 20, 11, 0, 0, Math.PI * 2);
    ctx.fill();

    ctx.strokeStyle = '#1a1025';
    ctx.fillStyle = '#1a1025';
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';

    // Забавный завиток на лбу
    ctx.lineWidth = 7;
    ctx.beginPath();
    ctx.moveTo(118, 60);
    ctx.bezierCurveTo(105, 85, 138, 92, 132, 70);
    ctx.bezierCurveTo(126, 55, 145, 82, 152, 72);
    ctx.stroke();

    if (expr === 'IDLE') {
      // Круглые милые глаза с белой обводкой (как у оранжевого персонажа PEAK)
      this.drawRoundEye(86, 120, 18);
      this.drawRoundEye(170, 120, 18);

      // Маленький ротик
      ctx.lineWidth = 7;
      ctx.beginPath();
      ctx.arc(128, 154, 14, 0.15 * Math.PI, 0.85 * Math.PI);
      ctx.stroke();
    } else if (expr === 'STRAIN') {
      // Эмоция >_< и стиснутые зубы (точно как у синего персонажа на обложке PEAK!)
      ctx.lineWidth = 10;

      // Левый глаз >
      ctx.beginPath();
      ctx.moveTo(64, 104);
      ctx.lineTo(96, 122);
      ctx.lineTo(64, 140);
      ctx.stroke();

      // Правый глаз <
      ctx.beginPath();
      ctx.moveTo(192, 104);
      ctx.lineTo(160, 122);
      ctx.lineTo(192, 140);
      ctx.stroke();

      // Стиснутые зубы (широкий рот с полосками зубов)
      ctx.fillStyle = '#ffffff';
      ctx.strokeStyle = '#1a1025';
      ctx.lineWidth = 7;
      ctx.beginPath();
      ctx.roundRect(88, 146, 80, 32, [16, 16, 8, 8]);
      ctx.fill();
      ctx.stroke();

      // Вертикальные линии зубов
      ctx.lineWidth = 5;
      for (let x = 108; x <= 148; x += 20) {
        ctx.beginPath();
        ctx.moveTo(x, 148);
        ctx.lineTo(x, 176);
        ctx.stroke();
      }
    } else if (expr === 'SHOCK') {
      // Огромные испуганные глаза O_O
      this.drawRoundEye(86, 118, 23);
      this.drawRoundEye(170, 118, 23);

      // Круглый открытый рот "О"
      ctx.fillStyle = '#1a1025';
      ctx.beginPath();
      ctx.ellipse(128, 162, 14, 18, 0, 0, Math.PI * 2);
      ctx.fill();
    } else if (expr === 'SNEAK') {
      // Хитрые полуприкрытые глаза -_-
      ctx.lineWidth = 9;
      ctx.beginPath();
      ctx.moveTo(65, 122);
      ctx.lineTo(105, 122);
      ctx.moveTo(151, 122);
      ctx.lineTo(191, 122);
      ctx.stroke();

      // Ухмылка
      ctx.lineWidth = 6;
      ctx.beginPath();
      ctx.arc(132, 152, 16, 0.1 * Math.PI, 0.75 * Math.PI);
      ctx.stroke();
    }

    this.texture.needsUpdate = true;
  }

  private drawRoundEye(x: number, y: number, r: number) {
    const ctx = this.ctx;
    ctx.fillStyle = '#ffffff';
    ctx.beginPath();
    ctx.arc(x, y, r + 4, 0, Math.PI * 2);
    ctx.fill();

    ctx.fillStyle = '#1a1025';
    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.fill();

    // Блик
    ctx.fillStyle = '#ffffff';
    ctx.beginPath();
    ctx.arc(x - r * 0.3, y - r * 0.3, r * 0.28, 0, Math.PI * 2);
    ctx.fill();
  }
}
