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
        if (oldMat && oldMat.color) {
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
      color: 0xe2e8f0,
      roughness: 0.9,
      metalness: 0.05
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
    const chimneyLight = new THREE.PointLight(0xff7700, 3.5, 6);
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
