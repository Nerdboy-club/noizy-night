import * as THREE from 'three';
import RAPIER from '@dimforge/rapier3d-compat';
import { PhysicsWorld } from '../physics/PhysicsWorld';
import { applyCartoonOutline, createPeakMaterial } from '../rendering/ToonStyle';

export class RooftopLevel {
  public group: THREE.Group;
  public chimneyPosition: THREE.Vector3;
  private snowParticles: THREE.Points;
  private smokeParticles: THREE.Points;
  private physics: PhysicsWorld;
  private bodies: RAPIER.RigidBody[] = [];

  constructor(scene: THREE.Scene, physics: PhysicsWorld) {
    this.physics = physics;
    this.group = new THREE.Group();
    this.chimneyPosition = new THREE.Vector3(0, 4.5, -6);

    // 1. Создание геометрии заснеженной крыши
    this.buildRooftop();

    // 2. Дымоход с теплым светом
    this.buildChimney();

    // 2b. Декор: снежные шапки, гирлянда, звёзды, светящееся жерло трубы
    this.buildRooftopDecor();

    // 3. Снегопад
    this.snowParticles = this.createSnowSystem();
    this.group.add(this.snowParticles);

    // 4. Дым из трубы
    this.smokeParticles = this.createSmokeSystem();
    this.group.add(this.smokeParticles);

    // 5. Мультяшный Cel-Shading и обводка в стиле PEAK
    this.group.traverse((child) => {
      if ((child as THREE.Mesh).isMesh) {
        const m = child as THREE.Mesh;
        const oldMat = m.material as THREE.MeshStandardMaterial;
        // Материалы с эмиссией / особые (декор) оставляем как есть
        const keep = (oldMat as THREE.MeshStandardMaterial).emissiveIntensity > 0 || m.userData.keepMaterial;
        if (oldMat && oldMat.color && !keep) {
          m.material = createPeakMaterial(oldMat.color.getHex());
        }
      }
    });
    applyCartoonOutline(this.group, 0.018);

    scene.add(this.group);
  }

  private buildRooftop() {
    const R = this.physics.getRapier();

    // Материалы
    const snowMat = new THREE.MeshStandardMaterial({
      color: 0xf1f5f9,
      roughness: 0.75,
      metalness: 0.0
    });
    const brickMat = new THREE.MeshStandardMaterial({
      color: 0x991b1b,
      roughness: 0.8
    });
    const woodMat = new THREE.MeshStandardMaterial({
      color: 0x451a03,
      roughness: 0.7
    });

    // Основная покатая крыша (двускатная)
    // Левый скат
    const slopeGeo = new THREE.BoxGeometry(14, 0.4, 18);
    const leftSlope = new THREE.Mesh(slopeGeo, snowMat);
    leftSlope.position.set(-5, 2.5, 0);
    leftSlope.rotation.z = Math.PI * 0.12;
    leftSlope.receiveShadow = true;
    this.group.add(leftSlope);

    // Правый скат
    const rightSlope = new THREE.Mesh(slopeGeo, snowMat);
    rightSlope.position.set(5, 2.5, 0);
    rightSlope.rotation.z = -Math.PI * 0.12;
    rightSlope.receiveShadow = true;
    this.group.add(rightSlope);

    // Конёк крыши (площадка наверху)
    const ridgeGeo = new THREE.BoxGeometry(3.5, 0.5, 18);
    const ridge = new THREE.Mesh(ridgeGeo, snowMat);
    ridge.position.set(0, 3.8, 0);
    ridge.receiveShadow = true;
    this.group.add(ridge);

    // Добавляем физические коллайдеры для крыши
    const leftBodyDesc = R.RigidBodyDesc.fixed()
      .setTranslation(-5, 2.5, 0)
      .setRotation({ x: 0, y: 0, z: Math.sin((Math.PI * 0.12) / 2), w: Math.cos((Math.PI * 0.12) / 2) });
    const leftBody = this.physics.world.createRigidBody(leftBodyDesc);
    this.physics.world.createCollider(R.ColliderDesc.cuboid(7, 0.2, 9), leftBody);
    this.bodies.push(leftBody);

    const rightBodyDesc = R.RigidBodyDesc.fixed()
      .setTranslation(5, 2.5, 0)
      .setRotation({ x: 0, y: 0, z: Math.sin((-Math.PI * 0.12) / 2), w: Math.cos((-Math.PI * 0.12) / 2) });
    const rightBody = this.physics.world.createRigidBody(rightBodyDesc);
    this.physics.world.createCollider(R.ColliderDesc.cuboid(7, 0.2, 9), rightBody);
    this.bodies.push(rightBody);

    const ridgeBodyDesc = R.RigidBodyDesc.fixed().setTranslation(0, 3.8, 0);
    const ridgeBody = this.physics.world.createRigidBody(ridgeBodyDesc);
    this.physics.world.createCollider(R.ColliderDesc.cuboid(1.75, 0.25, 9), ridgeBody);
    this.bodies.push(ridgeBody);

    // Дополнительные слуховые окна и трубы
    for (let i = -1; i <= 1; i += 2) {
      const dormer = new THREE.Mesh(new THREE.ConeGeometry(1.2, 1.5, 4), woodMat);
      dormer.position.set(i * 4.5, 3.2, 2);
      dormer.rotation.y = Math.PI / 4;
      this.group.add(dormer);
    }
  }

  private buildChimney() {
    const R = this.physics.getRapier();
    const brickMat = new THREE.MeshStandardMaterial({ color: 0xb91c1c, roughness: 0.85 });
    const sootMat = new THREE.MeshStandardMaterial({ color: 0x111827, roughness: 0.95 });

    // Внешняя кирпичная кладка дымохода
    const chimney = new THREE.Group();
    chimney.position.copy(this.chimneyPosition);

    const baseMesh = new THREE.Mesh(new THREE.BoxGeometry(1.8, 2.2, 1.8), brickMat);
    baseMesh.castShadow = true;
    baseMesh.receiveShadow = true;
    chimney.add(baseMesh);

    // Верхний карниз дымохода
    const rimMesh = new THREE.Mesh(new THREE.BoxGeometry(2.1, 0.25, 2.1), brickMat);
    rimMesh.position.y = 1.1;
    rimMesh.castShadow = true;
    chimney.add(rimMesh);

    // Черное отверстие внутри
    const holeMesh = new THREE.Mesh(new THREE.BoxGeometry(1.3, 0.2, 1.3), sootMat);
    holeMesh.position.y = 1.15;
    chimney.add(holeMesh);

    // Уютный теплый свет из камина внутри трубы
    const chimneyLight = new THREE.PointLight(0xff7a1a, 6.0, 9, 1.5);
    chimneyLight.position.set(0, 1.2, 0);
    chimney.add(chimneyLight);

    this.group.add(chimney);

    // Физический коллайдер дымохода
    const bodyDesc = R.RigidBodyDesc.fixed().setTranslation(
      this.chimneyPosition.x,
      this.chimneyPosition.y,
      this.chimneyPosition.z
    );
    const body = this.physics.world.createRigidBody(bodyDesc);
    this.physics.world.createCollider(R.ColliderDesc.cuboid(0.9, 1.1, 0.9), body);
    this.bodies.push(body);
  }

  private buildRooftopDecor() {
    const keep = <T extends THREE.Mesh>(m: T): T => {
      m.userData.keepMaterial = true;
      return m;
    };
    const cp = this.chimneyPosition;

    // Снежная шапка на трубе
    const snowCapMat = new THREE.MeshStandardMaterial({ color: 0xf8fafc, roughness: 0.7 });
    const drifts: [number, number, number, number][] = [
      [0, -1.0, 2.2, 0.25],
      [0, 1.0, 2.2, 0.25],
      [-1.0, 0, 0.25, 1.8],
      [1.0, 0, 0.25, 1.8]
    ];
    drifts.forEach(([dx, dz, sx, sz]) => {
      const d = keep(new THREE.Mesh(new THREE.BoxGeometry(sx, 0.16, sz), snowCapMat));
      d.position.set(cp.x + dx, cp.y + 1.3, cp.z + dz);
      this.group.add(d);
    });

    // Раскалённое жерло: эмиссивный диск (светится через bloom) + тёплое пятно на крыше
    const ember = keep(
      new THREE.Mesh(
        new THREE.CircleGeometry(0.62, 20),
        new THREE.MeshStandardMaterial({
          color: 0xff6a00,
          emissive: 0xff7a1a,
          emissiveIntensity: 3.2,
          side: THREE.DoubleSide
        })
      )
    );
    ember.rotation.x = -Math.PI / 2;
    ember.position.set(cp.x, cp.y + 1.27, cp.z);
    this.group.add(ember);

    const glow = keep(
      new THREE.Mesh(
        new THREE.CircleGeometry(3.2, 32),
        new THREE.MeshBasicMaterial({
          color: 0xff8a2a,
          transparent: true,
          opacity: 0.16,
          depthWrite: false,
          blending: THREE.AdditiveBlending
        })
      )
    );
    glow.rotation.x = -Math.PI / 2;
    glow.position.set(cp.x, cp.y + 1.32, cp.z);
    this.group.add(glow);

    // Сугробы на скатах
    const driftMat = new THREE.MeshStandardMaterial({ color: 0xf8fafc, roughness: 0.65 });
    const driftGeo = new THREE.SphereGeometry(1, 14, 10);
    for (let i = 0; i < 14; i++) {
      const side = i % 2 === 0 ? -1 : 1;
      const d = keep(new THREE.Mesh(driftGeo, driftMat));
      const t = (i / 14) * 2 - 1;
      d.scale.set(0.8 + (i % 3) * 0.35, 0.18 + (i % 4) * 0.05, 0.9 + (i % 2) * 0.4);
      d.position.set(side * (2.6 + (i % 4) * 1.5), 3.3 - (2.6 + (i % 4) * 1.5 - 1.75) * 0.24 + 0.15, t * 8);
      d.receiveShadow = true;
      this.group.add(d);
    }

    // Гирлянда вдоль конька (эмиссивные огоньки, без источников света)
    const colors = [0xff5252, 0xffd166, 0x7dd3fc, 0x86efac];
    const bulbMats = colors.map(
      (c) => new THREE.MeshStandardMaterial({ color: c, emissive: c, emissiveIntensity: 3.0 })
    );
    const bulbGeo = new THREE.SphereGeometry(0.09, 8, 8);
    for (let i = 0; i < 24; i++) {
      const z = -8.5 + i * (17 / 23);
      const b = keep(new THREE.Mesh(bulbGeo, bulbMats[i % bulbMats.length]));
      b.position.set(-1.7, 4.15 - Math.sin((i / 23) * Math.PI * 4) * 0.04, z);
      this.group.add(b);
      const b2 = keep(new THREE.Mesh(bulbGeo, bulbMats[(i + 2) % bulbMats.length]));
      b2.position.set(1.7, 4.15, z);
      this.group.add(b2);
    }

    // Сосульки на краях скатов
    const iceMat = new THREE.MeshStandardMaterial({
      color: 0xbfe3ff,
      emissive: 0x38bdf8,
      emissiveIntensity: 0.25,
      roughness: 0.1,
      transparent: true,
      opacity: 0.85
    });
    const iceGeo = new THREE.ConeGeometry(0.07, 0.4, 5);
    for (let i = 0; i < 26; i++) {
      const side = i % 2 === 0 ? -1 : 1;
      const z = -8.4 + (i >> 1) * 1.3;
      const ic = keep(new THREE.Mesh(iceGeo, iceMat));
      ic.rotation.x = Math.PI;
      ic.position.set(side * 11.6, 0.35 + (i % 3) * 0.05, z);
      this.group.add(ic);
    }

    // Звёздное небо
    const starCount = 260;
    const sp = new Float32Array(starCount * 3);
    for (let i = 0; i < starCount; i++) {
      const a = Math.random() * Math.PI * 2;
      const el = 0.15 + Math.random() * 1.2;
      const r = 70;
      sp[i * 3] = Math.cos(a) * Math.cos(el) * r;
      sp[i * 3 + 1] = Math.sin(el) * r;
      sp[i * 3 + 2] = Math.sin(a) * Math.cos(el) * r;
    }
    const sg = new THREE.BufferGeometry();
    sg.setAttribute('position', new THREE.BufferAttribute(sp, 3));
    const stars = new THREE.Points(
      sg,
      new THREE.PointsMaterial({ color: 0xffffff, size: 0.5, sizeAttenuation: true, fog: false, transparent: true, opacity: 0.9 })
    );
    this.group.add(stars);
  }

  private createSnowSystem(): THREE.Points {
    const count = 1200;
    const geometry = new THREE.BufferGeometry();
    const positions = new Float32Array(count * 3);

    for (let i = 0; i < count * 3; i += 3) {
      positions[i] = (Math.random() - 0.5) * 40;
      positions[i + 1] = Math.random() * 20;
      positions[i + 2] = (Math.random() - 0.5) * 40;
    }

    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));

    const material = new THREE.PointsMaterial({
      color: 0xffffff,
      size: 0.15,
      transparent: true,
      opacity: 0.8
    });

    return new THREE.Points(geometry, material);
  }

  private createSmokeSystem(): THREE.Points {
    const count = 80;
    const geometry = new THREE.BufferGeometry();
    const positions = new Float32Array(count * 3);

    for (let i = 0; i < count * 3; i += 3) {
      positions[i] = this.chimneyPosition.x + (Math.random() - 0.5) * 0.4;
      positions[i + 1] = this.chimneyPosition.y + 1.2 + Math.random() * 3.5;
      positions[i + 2] = this.chimneyPosition.z + (Math.random() - 0.5) * 0.4;
    }

    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));

    const material = new THREE.PointsMaterial({
      color: 0x94a3b8,
      size: 0.35,
      transparent: true,
      opacity: 0.35
    });

    return new THREE.Points(geometry, material);
  }

  public update(delta: number) {
    // Анимация снегопада
    const snowPos = this.snowParticles.geometry.attributes.position.array as Float32Array;
    for (let i = 1; i < snowPos.length; i += 3) {
      snowPos[i] -= delta * 3.5;
      if (snowPos[i] < -2) {
        snowPos[i] = 18;
      }
    }
    this.snowParticles.geometry.attributes.position.needsUpdate = true;

    // Анимация дыма
    const smokePos = this.smokeParticles.geometry.attributes.position.array as Float32Array;
    for (let i = 0; i < smokePos.length; i += 3) {
      smokePos[i + 1] += delta * 1.5;
      smokePos[i] += Math.sin(smokePos[i + 1] * 2) * delta * 0.3;
      if (smokePos[i + 1] > this.chimneyPosition.y + 5) {
        smokePos[i + 1] = this.chimneyPosition.y + 1.2;
        smokePos[i] = this.chimneyPosition.x + (Math.random() - 0.5) * 0.4;
      }
    }
    this.smokeParticles.geometry.attributes.position.needsUpdate = true;
  }

  public checkChimneyEntry(elfPos: THREE.Vector3): boolean {
    const dx = elfPos.x - this.chimneyPosition.x;
    const dy = elfPos.y - (this.chimneyPosition.y + 1.2);
    const dz = elfPos.z - this.chimneyPosition.z;
    const distHoriz = Math.hypot(dx, dz);
    return distHoriz < 1.0 && Math.abs(dy) < 1.0;
  }

  public setVisible(visible: boolean) {
    this.group.visible = visible;
    for (const b of this.bodies) {
      b.setEnabled(visible);
    }
  }
}
