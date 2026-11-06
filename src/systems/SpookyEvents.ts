import * as THREE from 'three';
import { SoundEngine } from '../audio/SoundEngine';

export class SpookyEvents {
  private scene: THREE.Scene;
  private soundEngine: SoundEngine;
  private timer: number = 0;
  private nextEventTime: number = 18;
  private slenderSilhouette: THREE.Group | null = null;
  private spookyOverlayEl: HTMLElement | null;
  private spookyTextEl: HTMLElement | null;

  constructor(scene: THREE.Scene, soundEngine: SoundEngine) {
    this.scene = scene;
    this.soundEngine = soundEngine;
    this.spookyOverlayEl = document.getElementById('spooky-overlay');
    this.spookyTextEl = document.getElementById('spooky-text');

    this.createSlenderSilhouette();
  }

  private createSlenderSilhouette() {
    const group = new THREE.Group();
    // Черный высокий силуэт за окном квартиры
    const shadowMat = new THREE.MeshBasicMaterial({ color: 0x020617 });

    // Очень высокое тонкое тело
    const body = new THREE.Mesh(new THREE.CylinderGeometry(0.12, 0.18, 3.2, 6), shadowMat);
    body.position.y = 1.6;
    group.add(body);

    // Голова
    const head = new THREE.Mesh(new THREE.SphereGeometry(0.2, 8, 8), shadowMat);
    head.position.y = 3.3;
    group.add(head);

    // Длинные свисающие руки
    const armL = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.04, 2.2, 5), shadowMat);
    armL.position.set(-0.35, 1.8, 0);
    group.add(armL);

    const armR = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.04, 2.2, 5), shadowMat);
    armR.position.set(0.35, 1.8, 0);
    group.add(armR);

    // Стоит прямо за окном (z = -10.8)
    group.position.set(0, 1.2, -10.8);
    group.visible = false;

    this.scene.add(group);
    this.slenderSilhouette = group;
  }

  public update(delta: number) {
    this.timer += delta;
    if (this.timer >= this.nextEventTime) {
      this.triggerSpookyEvent();
      this.timer = 0;
      this.nextEventTime = 22 + Math.random() * 20;
    }
  }

  public triggerSpookyEvent() {
    if (!this.slenderSilhouette) return;

    // Включаем силуэт за окном на 2.5 секунды
    this.slenderSilhouette.visible = true;
    this.soundEngine.playHorrorStinger();

    if (this.spookyOverlayEl && this.spookyTextEl) {
      this.spookyOverlayEl.style.opacity = '0.9';
      this.spookyTextEl.style.opacity = '1';
      this.spookyTextEl.style.transform = 'scale(1.05)';

      setTimeout(() => {
        if (this.spookyOverlayEl && this.spookyTextEl && this.slenderSilhouette) {
          this.spookyOverlayEl.style.opacity = '0';
          this.spookyTextEl.style.opacity = '0';
          this.spookyTextEl.style.transform = 'scale(0.9)';
          this.slenderSilhouette.visible = false;
        }
      }, 2400);
    }
  }
}
