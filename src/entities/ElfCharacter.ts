import * as THREE from 'three';
import RAPIER from '@dimforge/rapier3d-compat';
import { PhysicsWorld } from '../physics/PhysicsWorld';
import { SoundEngine } from '../audio/SoundEngine';
import { GiftItem } from './GiftItem';
import { applyCartoonOutline, createPeakMaterial } from '../rendering/ToonStyle';
import { FaceController, FaceExpression } from '../rendering/FaceTexture';
import { loadPeakModel } from '../loaders/ModelLoader';

export interface ElfColors {
  skin: number;
  shirt: number;    // Цвет вязаного свитера
  hat: number;      // Цвет вязаной балаклавы
  sash: number;     // Цвет шарфа
  name: string;
}

// Палитра отряда эльфов-грабителей рождества (Tactile Winter Heist)
export const ELF_THEMES: ElfColors[] = [
  { skin: 0xf6c29d, shirt: 0x3b6e38, hat: 0xb82929, sash: 0xdc2626, name: 'Красношап (Лидер)' },
  { skin: 0xf6c29d, shirt: 0x1d4ed8, hat: 0xd97706, sash: 0x16a34a, name: 'Синий Свитер' },
  { skin: 0xf6c29d, shirt: 0x7e22ce, hat: 0x15803d, sash: 0xeab308, name: 'Фиолетовый Вязаный' },
  { skin: 0xf6c29d, shirt: 0xb45309, hat: 0x2563eb, sash: 0xdc2626, name: 'Рыжий Шарф' }
];

export class ElfCharacter {
  public playerIndex: number;
  public mesh: THREE.Group;
  public body!: RAPIER.RigidBody;
  public collider!: RAPIER.Collider;

  // Сегменты утвержденного Heist Elf
  private head: THREE.Group;
  private faceCtrl: FaceController;
  private hatPomPom: THREE.Mesh;
  private leftArm: THREE.Group;
  private rightArm: THREE.Group;
  private leftLeg: THREE.Group;
  private rightLeg: THREE.Group;
  private heldGiftMesh: THREE.Group;
  private sackMesh: THREE.Mesh;
  private jetpackGroup: THREE.Group;
  private jetpackFlames: THREE.Mesh[] = [];

  // Состояния движения и физики (в духе PEAK)
  public isCrouching: boolean = false;
  public isGrabbing: boolean = false;
  public isClimbing: boolean = false;
  public isJetpacking: boolean = false;
  public stamina: number = 100;
  public carriedGift: GiftItem | null = null;
  public isHoldingGift: boolean = false;
  public isGrounded: boolean = false;
  public isLocalPlayer: boolean = true;

  // --- ПРОГРЕССИЯ И ГАДЖЕТЫ БАЗЫ САНТЫ (Вариант 1) ---
  public tipsWallet: number = 120;         // Заработанные чаевые ($) для прокачки на Базе
  public completedRuns: number = 0;        // Счетчик доставленных адресов
  public sackCapacity: number = 1;         // Вместимость мешка за спиной (1..5 подарков)
  public sackGifts: GiftItem[] = [];       // Подарки, сложенные в мешок [R]
  public silentBootsLevel: number = 0;     // Уровень бесшумной обуви (0..5)
  public jetpackLevel: number = 0;         // Уровень реактивного ранца (0 = не куплен, 1..5 = куплен и улучшен)
  public jetpackFuel: number = 100;        // Запас топлива ранца (0..100%)
  public gripLevel: number = 0;            // Уровень силы хвата и защиты упаковки (0..5)
  public gadgetDurability: number = 100;   // Исправность снаряжения (0..100%, чинится на базе)

  private walkCycle: number = 0;
  private stepTimer: number = 0;
  private soundEngine: SoundEngine;
  private physics: PhysicsWorld;
  private slipVelocity: THREE.Vector2 = new THREE.Vector2(0, 0);

  private keys: Record<string, boolean> = {};

  constructor(
    scene: THREE.Scene,
    physics: PhysicsWorld,
    soundEngine: SoundEngine,
    startPos: THREE.Vector3,
    playerIndex: number = 0,
    isLocalPlayer: boolean = true
  ) {
    this.playerIndex = playerIndex;
    this.isLocalPlayer = isLocalPlayer;
    this.physics = physics;
    this.soundEngine = soundEngine;
    this.faceCtrl = new FaceController();

    const theme = ELF_THEMES[playerIndex % ELF_THEMES.length];

    const built = this.buildHeistElfModel(theme);
    this.mesh = built.elfGroup;
    this.head = built.head;
    this.hatPomPom = built.pomPom;
    this.leftArm = built.leftArm;
    this.rightArm = built.rightArm;
    this.leftLeg = built.leftLeg;
    this.rightLeg = built.rightLeg;
    this.heldGiftMesh = built.heldGift;
    this.sackMesh = built.sackMesh;
    this.jetpackGroup = built.jetpackGroup;
    this.jetpackFlames = built.jetpackFlames;
    scene.add(this.mesh);

    // Автоподгрузка пользовательской 3D модели из public/models/elf.glb, если она есть
    loadPeakModel('/models/elf.glb', 1.15).then((customModel) => {
      if (customModel) {
        this.mesh.children.forEach((c) => {
          if (c !== this.heldGiftMesh && c !== this.sackMesh && c !== this.jetpackGroup) {
            c.visible = false;
          }
        });
        this.mesh.add(customModel);
      }
    });

    this.initPhysics(startPos);

    if (this.isLocalPlayer) {
      this.setupControls();
    }
  }

  private buildHeistElfModel(theme: ElfColors) {
    const elfGroup = new THREE.Group();

    const skinMat = createPeakMaterial(theme.skin, { roughness: 0.65 });
    const sweaterMat = createPeakMaterial(theme.shirt, { knitBump: true });
    const beanieMat = createPeakMaterial(theme.hat, { knitBump: true });
    const scarfRedMat = createPeakMaterial(theme.sash, { knitBump: true });
    const scarfWhiteMat = createPeakMaterial(0xf5f5f4, { knitBump: true });
    const pomMat = createPeakMaterial(0xfafaf9, { roughness: 0.95 });
    const pantsMat = createPeakMaterial(0x3a2f45);
    const bootMat = createPeakMaterial(0x6b3f22, { roughness: 0.7 });
    const soleMat = createPeakMaterial(0xf1e3c8, { roughness: 0.85 });
    const beltMat = createPeakMaterial(0x2b1d14, { roughness: 0.6 });
    const buckleMat = createPeakMaterial(0xfbbf24, { metalness: 0.6, roughness: 0.3 });
    const bellMat = createPeakMaterial(0xfbbf24, { emissive: 0xd97706, emissiveIntensity: 0.25, metalness: 0.5, roughness: 0.35 });
    const eyeWhiteMat = createPeakMaterial(0xffffff, { roughness: 0.3 });
    const irisMat = createPeakMaterial(0x2f6b3a, { roughness: 0.3 });
    const pupilMat = createPeakMaterial(0x0b0b0f, { roughness: 0.2 });
    const cheekMat = createPeakMaterial(0xff8fa3, { roughness: 0.9 });
    const sackMat = createPeakMaterial(0x991b1b, { knitBump: true });

    // Общие геометрии (экономим память)
    const sph = (r: number) => new THREE.SphereGeometry(r, 18, 14);

    // 1. Таз / штанишки
    const pants = new THREE.Mesh(sph(0.2), pantsMat);
    pants.scale.set(1.1, 0.8, 1.0);
    pants.position.y = 0.28;
    elfGroup.add(pants);

    // 2. Круглый вязаный свитер (Торс)
    const torso = new THREE.Mesh(new THREE.CapsuleGeometry(0.19, 0.16, 8, 18), sweaterMat);
    torso.scale.set(1.05, 1.0, 0.95);
    torso.position.y = 0.46;
    elfGroup.add(torso);

    const hemRib = new THREE.Mesh(new THREE.TorusGeometry(0.2, 0.035, 10, 24), sweaterMat);
    hemRib.rotation.x = Math.PI / 2;
    hemRib.position.y = 0.34;
    elfGroup.add(hemRib);

    // Ремень с пряжкой
    const belt = new THREE.Mesh(new THREE.TorusGeometry(0.2, 0.017, 8, 24), beltMat);
    belt.rotation.x = Math.PI / 2;
    belt.position.y = 0.385;
    belt.scale.set(1.0, 0.96, 1);
    elfGroup.add(belt);
    const buckle = new THREE.Mesh(new THREE.TorusGeometry(0.032, 0.01, 8, 14), buckleMat);
    buckle.position.set(0, 0.385, 0.2);
    elfGroup.add(buckle);

    // Полосатый шарф: пухлое кольцо + свисающий хвост
    const scarfRing = new THREE.Mesh(new THREE.TorusGeometry(0.17, 0.055, 12, 26), scarfRedMat);
    scarfRing.rotation.x = Math.PI / 2;
    scarfRing.position.y = 0.64;
    elfGroup.add(scarfRing);
    const scarfStripe = new THREE.Mesh(new THREE.TorusGeometry(0.17, 0.057, 12, 26, Math.PI), scarfWhiteMat);
    scarfStripe.rotation.x = Math.PI / 2;
    scarfStripe.position.y = 0.64;
    scarfStripe.rotation.z = 0.0;
    elfGroup.add(scarfStripe);

    const scarfTail = new THREE.Group();
    scarfTail.position.set(-0.08, 0.57, 0.17);
    scarfTail.rotation.z = 0.08;
    const stripeGeo = new THREE.CapsuleGeometry(0.035, 0.03, 4, 10);
    for (let i = 0; i < 5; i++) {
      const stripe = new THREE.Mesh(stripeGeo, i % 2 === 0 ? scarfRedMat : scarfWhiteMat);
      stripe.rotation.z = Math.PI / 2;
      stripe.scale.set(0.9, 1.05, 0.55);
      stripe.position.y = -i * 0.05;
      scarfTail.add(stripe);
    }
    elfGroup.add(scarfTail);

    // 3. МЕШОК САНТЫ ЗА СПИНОЙ (масштабируется в updateVisualGadgets)
    const sackMesh = new THREE.Mesh(sph(0.2), sackMat);
    sackMesh.position.set(0, 0.46, -0.22);
    sackMesh.scale.set(0.85, 0.95, 0.75);
    elfGroup.add(sackMesh);

    // 4. РЕАКТИВНЫЙ РАНЕЦ
    const jetpackGroup = new THREE.Group();
    jetpackGroup.position.set(0, 0.48, -0.2);
    jetpackGroup.visible = false;

    const tankMat = createPeakMaterial(0x0284c7, { metalness: 0.4, roughness: 0.35 });
    const metalMat = createPeakMaterial(0x94a3b8, { metalness: 0.7, roughness: 0.3 });
    const flameMat = new THREE.MeshBasicMaterial({ color: 0xfacc15 });
    const jetpackFlames: THREE.Mesh[] = [];

    [-0.12, 0.12].forEach((tx) => {
      const tank = new THREE.Mesh(new THREE.CapsuleGeometry(0.06, 0.18, 6, 12), tankMat);
      tank.position.set(tx, 0, 0);
      jetpackGroup.add(tank);

      const nozzle = new THREE.Mesh(new THREE.ConeGeometry(0.05, 0.08, 10), metalMat);
      nozzle.position.set(tx, -0.17, 0);
      jetpackGroup.add(nozzle);

      const flame = new THREE.Mesh(new THREE.ConeGeometry(0.055, 0.24, 8), flameMat);
      flame.rotation.x = Math.PI;
      flame.position.set(tx, -0.28, 0);
      flame.visible = false;
      jetpackGroup.add(flame);
      jetpackFlames.push(flame);
    });
    elfGroup.add(jetpackGroup);

    // 5. ГОЛОВА: большая круглая, лицо с читаемыми глазами, вязаная шапка с помпоном
    const head = new THREE.Group();
    head.position.set(0, 0.86, 0);

    const skull = new THREE.Mesh(sph(0.25), skinMat);
    skull.scale.set(1.0, 0.96, 0.96);
    head.add(skull);

    // Нос и щёчки
    const nose = new THREE.Mesh(sph(0.035), skinMat);
    nose.scale.set(1, 0.9, 1);
    nose.position.set(0, -0.025, 0.245);
    nose.material = createPeakMaterial(0xf09d8a, { roughness: 0.6 });
    head.add(nose);

    [-0.13, 0.13].forEach((cx) => {
      const cheek = new THREE.Mesh(sph(0.04), cheekMat);
      cheek.scale.set(1.2, 0.7, 0.4);
      cheek.position.set(cx, -0.07, 0.215);
      cheek.rotation.y = cx * 2;
      head.add(cheek);
    });

    // Большие глаза: белок, радужка, зрачок, блик
    [-0.085, 0.085].forEach((ex, idx) => {
      const white = new THREE.Mesh(sph(0.05), eyeWhiteMat);
      white.scale.set(0.95, 1.2, 0.5);
      white.position.set(ex, 0.03, 0.222);
      head.add(white);

      const iris = new THREE.Mesh(sph(0.034), irisMat);
      iris.scale.set(1, 1.15, 0.5);
      iris.position.set(ex, 0.028, 0.243);
      head.add(iris);

      const pupil = new THREE.Mesh(sph(0.019), pupilMat);
      pupil.scale.set(1, 1.1, 0.5);
      pupil.position.set(ex, 0.028, 0.255);
      head.add(pupil);

      const glint = new THREE.Mesh(sph(0.009), eyeWhiteMat);
      glint.position.set(ex + (idx === 0 ? 0.012 : 0.012), 0.045, 0.262);
      head.add(glint);

      const brow = new THREE.Mesh(new THREE.CapsuleGeometry(0.01, 0.045, 4, 8), beanieMat);
      brow.rotation.z = Math.PI / 2 + (idx === 0 ? -0.2 : 0.2);
      brow.position.set(ex, 0.095, 0.225);
      head.add(brow);
    });

    // Улыбка
    const smile = new THREE.Mesh(new THREE.TorusGeometry(0.035, 0.007, 6, 14, Math.PI), pupilMat);
    smile.rotation.z = Math.PI;
    smile.position.set(0, -0.08, 0.238);
    head.add(smile);

    // Острые эльфийские уши
    const earGeo = new THREE.ConeGeometry(0.06, 0.2, 12);
    earGeo.translate(0, 0.1, 0);
    const leftEar = new THREE.Mesh(earGeo, skinMat);
    leftEar.rotation.z = Math.PI * 0.5 + 0.35;
    leftEar.position.set(-0.235, 0.0, 0);
    head.add(leftEar);
    const rightEar = new THREE.Mesh(earGeo, skinMat);
    rightEar.rotation.z = -Math.PI * 0.5 - 0.35;
    rightEar.position.set(0.235, 0.0, 0);
    head.add(rightEar);

    // Вязаная шапка: Lathe-колпак, отогнутая манжета, помпон
    const hatGroup = new THREE.Group();
    hatGroup.position.set(0, 0.1, -0.015);
    hatGroup.rotation.x = -0.18;
    const hatProfile: THREE.Vector2[] = [
      new THREE.Vector2(0.262, 0.0),
      new THREE.Vector2(0.255, 0.06),
      new THREE.Vector2(0.225, 0.14),
      new THREE.Vector2(0.17, 0.24),
      new THREE.Vector2(0.1, 0.34),
      new THREE.Vector2(0.05, 0.42),
      new THREE.Vector2(0.0, 0.46)
    ];
    const hatCone = new THREE.Mesh(new THREE.LatheGeometry(hatProfile, 28), beanieMat);
    hatCone.scale.set(1, 1, 0.98);
    hatGroup.add(hatCone);

    const brim = new THREE.Mesh(new THREE.TorusGeometry(0.255, 0.05, 12, 30), beanieMat);
    brim.rotation.x = Math.PI / 2;
    brim.position.y = 0.01;
    hatGroup.add(brim);
    const brimRib = new THREE.Mesh(new THREE.TorusGeometry(0.262, 0.026, 8, 30), scarfWhiteMat);
    brimRib.rotation.x = Math.PI / 2;
    brimRib.position.y = -0.03;
    hatGroup.add(brimRib);

    const pomPom = new THREE.Mesh(sph(0.075), pomMat);
    pomPom.position.set(0, 0.47, 0);
    hatGroup.add(pomPom);
    head.add(hatGroup);

    elfGroup.add(head);

    // 6. Ноги: круглые ботинки с подошвой, загнутым носом и бубенчиком
    const thighGeo = new THREE.CapsuleGeometry(0.06, 0.08, 6, 12);
    const bootGeo = sph(0.095);
    const soleGeo = new THREE.CylinderGeometry(0.1, 0.105, 0.03, 18);
    const curlGeo = new THREE.ConeGeometry(0.05, 0.12, 12);
    const bellGeo = sph(0.024);
    const cuffGeo = new THREE.TorusGeometry(0.068, 0.02, 8, 16);

    const buildLeg = (x: number) => {
      const leg = new THREE.Group();
      leg.position.set(x, 0.22, 0);

      const thigh = new THREE.Mesh(thighGeo, pantsMat);
      thigh.position.y = -0.06;
      leg.add(thigh);

      const boot = new THREE.Mesh(bootGeo, bootMat);
      boot.scale.set(1.0, 0.95, 1.4);
      boot.position.set(0, -0.15, 0.03);
      leg.add(boot);

      const cuff = new THREE.Mesh(cuffGeo, scarfWhiteMat);
      cuff.rotation.x = Math.PI / 2;
      cuff.position.set(0, -0.1, 0.0);
      leg.add(cuff);

      const sole = new THREE.Mesh(soleGeo, soleMat);
      sole.scale.set(1, 1, 1.5);
      sole.position.set(0, -0.225, 0.04);
      leg.add(sole);

      const curl = new THREE.Mesh(curlGeo, bootMat);
      curl.position.set(0, -0.12, 0.17);
      curl.rotation.x = Math.PI * 0.35;
      leg.add(curl);

      const bell = new THREE.Mesh(bellGeo, bellMat);
      bell.position.set(0, -0.085, 0.235);
      leg.add(bell);

      return leg;
    };

    const leftLeg = buildLeg(-0.11);
    const rightLeg = buildLeg(0.11);
    elfGroup.add(leftLeg);
    elfGroup.add(rightLeg);

    // 7. Руки: пухлые рукава, манжеты и варежки с большим пальцем
    const sleeveGeo = new THREE.CapsuleGeometry(0.058, 0.15, 6, 12);
    const armCuffGeo = new THREE.TorusGeometry(0.058, 0.02, 8, 14);
    const mittenGeo = sph(0.075);
    const thumbGeo = sph(0.03);
    const buildArm = (side: number) => {
      const arm = new THREE.Group();
      arm.position.set(side * 0.23, 0.57, 0);

      const sleeve = new THREE.Mesh(sleeveGeo, sweaterMat);
      sleeve.position.y = -0.12;
      arm.add(sleeve);

      const cuff = new THREE.Mesh(armCuffGeo, scarfWhiteMat);
      cuff.rotation.x = Math.PI / 2;
      cuff.position.y = -0.255;
      arm.add(cuff);

      const mitten = new THREE.Mesh(mittenGeo, beanieMat);
      mitten.scale.set(0.95, 1.05, 0.9);
      mitten.position.set(0, -0.32, 0);
      arm.add(mitten);

      const thumb = new THREE.Mesh(thumbGeo, beanieMat);
      thumb.position.set(-side * 0.06, -0.3, 0.035);
      arm.add(thumb);

      return arm;
    };

    const leftArm = buildArm(-1);
    const rightArm = buildArm(1);
    elfGroup.add(leftArm);
    elfGroup.add(rightArm);

    // 8. Вспомогательный подарок в руках
    const heldGift = new THREE.Group();
    heldGift.position.set(0, 0.48, 0.36);
    heldGift.visible = false;
    elfGroup.add(heldGift);

    applyCartoonOutline(elfGroup, 0.026);

    return {
      elfGroup,
      head,
      pomPom,
      leftArm,
      rightArm,
      leftLeg,
      rightLeg,
      heldGift,
      sackMesh,
      jetpackGroup,
      jetpackFlames
    };
  }

  private initPhysics(startPos: THREE.Vector3) {
    const R = this.physics.getRapier();
    const bodyDesc = R.RigidBodyDesc.dynamic()
      .setTranslation(startPos.x, startPos.y, startPos.z)
      .lockRotations()
      .setLinearDamping(4.0);

    this.body = this.physics.world.createRigidBody(bodyDesc);
    const colliderDesc = R.ColliderDesc.capsule(0.25, 0.22).setFriction(0.6).setRestitution(0.05);
    this.collider = this.physics.world.createCollider(colliderDesc, this.body);
  }

  private setupControls() {
    window.addEventListener('keydown', (e) => {
      this.keys[e.code] = true;
      if (e.code === 'KeyC') {
        this.toggleCrouch();
      }
      if (e.code === 'ShiftLeft' || e.code === 'ShiftRight' || e.code === 'KeyF') {
        this.isGrabbing = true;
      }
    });

    window.addEventListener('keyup', (e) => {
      this.keys[e.code] = false;
      if (e.code === 'ShiftLeft' || e.code === 'ShiftRight' || e.code === 'KeyF') {
        this.isGrabbing = false;
        this.isClimbing = false;
      }
    });
  }

  public toggleCrouch() {
    this.isCrouching = !this.isCrouching;
  }

  public updateVisualGadgets() {
    // Визуальный размер мешка зависит от количества сложенных туда подарков
    const count = this.sackGifts.length;
    const s = 0.75 + count * 0.28;
    this.sackMesh.scale.set(s, s * 1.05, s * 0.9);
    this.sackMesh.position.z = -0.2 - count * 0.04;

    // Реактивный ранец виден, если куплен (jetpackLevel > 0)
    this.jetpackGroup.visible = this.jetpackLevel > 0;
  }

  public update(
    delta: number,
    cameraAngleY: number,
    surfaceType: 'snow' | 'wood' | 'carpet' | 'tile' | 'ice',
    isCreakyFloor: boolean,
    currentNoise: number = 10
  ): { noiseGenerated: number; moved: boolean } {
    let noiseLevel = 0;

    this.updateVisualGadgets();

    // Учет веса подарка в руках + легкий вес мешка за спиной
    const handMultiplier = this.carriedGift ? this.carriedGift.getCarrySpeedMultiplier(this.gripLevel) : 1.0;
    const sackPenalty = Math.max(0.78, 1.0 - this.sackGifts.length * 0.04);
    const baseSpeed = this.isCrouching ? 2.6 : 5.4;
    const speed = baseSpeed * handMultiplier * sackPenalty;

    let moveX = 0;
    let moveZ = 0;

    if (this.isLocalPlayer) {
      if (this.keys['KeyW'] || this.keys['ArrowUp']) moveZ -= 1;
      if (this.keys['KeyS'] || this.keys['ArrowDown']) moveZ += 1;
      if (this.keys['KeyA'] || this.keys['ArrowLeft']) moveX -= 1;
      if (this.keys['KeyD'] || this.keys['ArrowRight']) moveX += 1;
    }

    const isMoving = moveX !== 0 || moveZ !== 0;

    // Проверка земли
    const pos = this.body.translation();
    const R = this.physics.getRapier();
    const ray = new R.Ray(pos, { x: 0, y: -1, z: 0 });
    const hit = this.physics.world.castRay(ray, 0.58, true);
    this.isGrounded = hit !== null;

    // Проверка стены впереди для скалолазания в стиле PEAK
    const forwardVec = new THREE.Vector3(0, 0, 1).applyAxisAngle(new THREE.Vector3(0, 1, 0), this.mesh.rotation.y);
    const wallRay = new R.Ray(pos, { x: forwardVec.x, y: 0, z: forwardVec.z });
    const wallHit = this.physics.world.castRay(wallRay, 0.65, true);

    if (this.isGrabbing && wallHit !== null && this.stamina > 5) {
      this.isClimbing = true;
    } else {
      this.isClimbing = false;
    }

    // --- РЕАКТИВНЫЙ РАНЕЦ (Клавиша Q) ---
    const wantsJetpack = this.isLocalPlayer && !!this.keys['KeyQ'] && this.jetpackLevel > 0 && this.jetpackFuel > 1;
    this.isJetpacking = wantsJetpack;
    this.jetpackFlames.forEach((f) => {
      f.visible = this.isJetpacking;
      if (this.isJetpacking) {
        f.scale.y = 0.8 + Math.random() * 0.5;
      }
    });

    if (this.isJetpacking) {
      const fuelBurnRate = Math.max(8, 22 - this.jetpackLevel * 3);
      this.jetpackFuel = Math.max(0, this.jetpackFuel - delta * fuelBurnRate);
      this.gadgetDurability = Math.max(0, this.gadgetDurability - delta * 1.5);
      const linVel = this.body.linvel();
      const targetUpVel = 5.2 + this.jetpackLevel * 0.6;
      this.body.setLinvel({ x: linVel.x, y: THREE.MathUtils.lerp(linVel.y, targetUpVel, 0.25), z: linVel.z }, true);
      noiseLevel = Math.max(noiseLevel, Math.max(18, 36 - this.silentBootsLevel * 3));
    } else if (this.isGrounded && this.jetpackLevel > 0) {
      // Медленная подзарядка давления ранца на земле
      this.jetpackFuel = Math.min(100, this.jetpackFuel + delta * (6 + this.jetpackLevel * 2));
    }

    // Расход и восстановление стамины (как в PEAK)
    const isHeavyStrain =
      (this.carriedGift && this.carriedGift.type === 'HUGE' && this.carriedGift.carriers.size < 2 && this.gripLevel < 2) ||
      this.isClimbing;
    if (isHeavyStrain) {
      this.stamina = Math.max(0, this.stamina - delta * 15);
    } else if (this.isGrounded) {
      this.stamina = Math.min(100, this.stamina + delta * 28);
    }

    let targetFace: FaceExpression = 'IDLE';
    if (currentNoise > 48) {
      targetFace = 'SHOCK';
    } else if (isHeavyStrain || (this.isGrabbing && !this.isGrounded)) {
      targetFace = 'STRAIN';
    } else if (this.isCrouching) {
      targetFace = 'SNEAK';
    }
    this.faceCtrl.setExpression(targetFace);

    // Коэффициент скольжения на плитке кухни и льду гаража (снижается с прокачкой Бесшумной Обуви!)
    const isSlipperySurface = surfaceType === 'tile' || surfaceType === 'ice';
    const bootTraction = Math.min(0.95, 0.14 + this.silentBootsLevel * 0.18);
    const lerpFactor = isSlipperySurface ? (surfaceType === 'ice' ? bootTraction * 0.55 : bootTraction) : 0.35;

    if (this.isClimbing) {
      const climbVelY = moveZ < 0 ? 3.6 : moveZ > 0 ? -2.5 : 0.3;
      this.body.setLinvel({ x: forwardVec.x * 1.2, y: climbVelY, z: forwardVec.z * 1.2 }, true);
      this.walkCycle += delta * 12;
      const climbWobble = Math.sin(this.walkCycle);
      this.leftArm.rotation.x = -2.4 + climbWobble * 0.5;
      this.rightArm.rotation.x = -2.4 - climbWobble * 0.5;
      this.leftLeg.rotation.x = climbWobble * 0.5;
      this.rightLeg.rotation.x = -climbWobble * 0.5;
    } else if (isMoving) {
      const inputAngle = Math.atan2(moveX, moveZ);
      const moveAngle = cameraAngleY + inputAngle;

      const targetVx = Math.sin(moveAngle) * speed;
      const targetVz = Math.cos(moveAngle) * speed;

      this.slipVelocity.x = THREE.MathUtils.lerp(this.slipVelocity.x, targetVx, lerpFactor);
      this.slipVelocity.y = THREE.MathUtils.lerp(this.slipVelocity.y, targetVz, lerpFactor);

      const linVel = this.body.linvel();
      this.body.setLinvel({ x: this.slipVelocity.x, y: linVel.y, z: this.slipVelocity.y }, true);

      this.mesh.rotation.y = THREE.MathUtils.lerp(this.mesh.rotation.y, moveAngle, 0.22);

      const wobbleFrequency = handMultiplier < 0.5 ? 7 : this.isCrouching ? 8 : 15;
      this.walkCycle += delta * wobbleFrequency;
      const wobble = Math.sin(this.walkCycle);

      this.leftLeg.rotation.x = wobble * 0.75;
      this.rightLeg.rotation.x = -wobble * 0.75;

      if (this.isGrabbing) {
        this.leftArm.rotation.x = -1.7 + wobble * 0.15;
        this.rightArm.rotation.x = -1.7 - wobble * 0.15;
      } else if (this.carriedGift || this.isHoldingGift) {
        // Обе руки вытянуты вперед и держат подарок!
        this.leftArm.rotation.x = -1.15 + wobble * 0.12;
        this.rightArm.rotation.x = -1.15 - wobble * 0.12;

        if (this.carriedGift && this.carriedGift.type === 'HUGE') {
          this.leftArm.rotation.z = -0.38;
          this.rightArm.rotation.z = 0.38;
        }
      } else {
        this.leftArm.rotation.x = -wobble * 0.65;
        this.rightArm.rotation.x = wobble * 0.65;
        this.leftArm.rotation.z = -0.12;
        this.rightArm.rotation.z = 0.12;
      }

      this.head.rotation.z = Math.sin(this.walkCycle * 0.5) * (isHeavyStrain ? 0.25 : 0.12);
      this.head.rotation.x = isHeavyStrain ? -0.18 : 0;
      this.hatPomPom.position.x = Math.sin(this.walkCycle) * 0.035;

      // Шаги и шум (учитываем уровень прокачки Бесшумной Обуви!)
      this.stepTimer += delta;
      const stepInterval = this.isCrouching ? 0.45 : 0.28;
      if (this.stepTimer >= stepInterval && this.isGrounded) {
        this.stepTimer = 0;
        const bootNoiseReduction = Math.min(0.8, this.silentBootsLevel * 0.16);

        if (this.isCrouching) {
          this.soundEngine.playFootstep('crouch');
          noiseLevel = Math.max(noiseLevel, Math.round(8 * (1 - bootNoiseReduction * 0.5)));
        } else {
          this.soundEngine.playFootstep(surfaceType === 'carpet' ? 'carpet' : 'wood');
          const rawStepNoise = surfaceType === 'carpet' ? 12 : surfaceType === 'tile' ? 32 : 26;
          noiseLevel = Math.max(noiseLevel, Math.round(rawStepNoise * (1 - bootNoiseReduction)));
        }

        if (isCreakyFloor) {
          this.soundEngine.playCreak();
          const rawCreak = this.isCrouching ? 32 : 72;
          noiseLevel = Math.max(noiseLevel, Math.round(rawCreak * (1 - bootNoiseReduction * 0.7)));
        }
      }
    } else {
      this.leftLeg.rotation.x = THREE.MathUtils.lerp(this.leftLeg.rotation.x, 0, 0.15);
      this.rightLeg.rotation.x = THREE.MathUtils.lerp(this.rightLeg.rotation.x, 0, 0.15);
      if (this.isGrabbing) {
        this.leftArm.rotation.x = THREE.MathUtils.lerp(this.leftArm.rotation.x, -1.75, 0.2);
        this.rightArm.rotation.x = THREE.MathUtils.lerp(this.rightArm.rotation.x, -1.75, 0.2);
      } else if (this.carriedGift || this.isHoldingGift) {
        this.leftArm.rotation.x = -1.05;
        this.rightArm.rotation.x = -1.05;
      } else {
        this.leftArm.rotation.x = THREE.MathUtils.lerp(this.leftArm.rotation.x, 0, 0.15);
        this.rightArm.rotation.x = THREE.MathUtils.lerp(this.rightArm.rotation.x, 0, 0.15);
      }
      this.head.rotation.z = THREE.MathUtils.lerp(this.head.rotation.z, 0, 0.15);
      this.head.rotation.x = THREE.MathUtils.lerp(this.head.rotation.x, 0, 0.15);

      const stopDecel = isSlipperySurface ? Math.min(0.96, 0.94 - this.silentBootsLevel * 0.04) : 0.72;
      this.slipVelocity.multiplyScalar(stopDecel);
      const linVel = this.body.linvel();
      this.body.setLinvel({ x: this.slipVelocity.x, y: linVel.y, z: this.slipVelocity.y }, true);
    }

    // Прыжок
    if (this.isLocalPlayer && this.keys['Space'] && (this.isGrounded || this.isClimbing)) {
      const jumpImpulse = handMultiplier < 0.5 ? 4.8 : 7.6;
      const linVel = this.body.linvel();
      this.body.setLinvel({ x: linVel.x, y: jumpImpulse, z: linVel.z }, true);
      this.isClimbing = false;
      this.soundEngine.playFootstep('snow');
      const bootNoiseReduction = Math.min(0.75, this.silentBootsLevel * 0.15);
      noiseLevel = Math.max(noiseLevel, Math.round(42 * (1 - bootNoiseReduction)));
    }

    // Приседание
    const targetScaleY = this.isCrouching ? 0.65 : 1.0;
    this.mesh.scale.y = THREE.MathUtils.lerp(this.mesh.scale.y, targetScaleY, 0.2);

    const currentPos = this.body.translation();
    this.mesh.position.set(currentPos.x, currentPos.y - 0.45 * this.mesh.scale.y, currentPos.z);

    this.heldGiftMesh.visible = this.isHoldingGift && !this.carriedGift;

    // Физическое удержание подарка в руках перед эльфом (разворачивается вместе с эльфом!)
    if (this.carriedGift) {
      const elfForward = new THREE.Vector3(0, 0, 1).applyQuaternion(this.mesh.quaternion);
      const distAhead = this.carriedGift.type === 'HUGE' ? 1.05 : 0.58;
      const heightOffset = this.carriedGift.type === 'HUGE' ? 0.68 : 0.55;
      const targetGiftPos = this.mesh.position
        .clone()
        .add(elfForward.multiplyScalar(distAhead))
        .setY(this.mesh.position.y + heightOffset);

      this.carriedGift.body.setTranslation(targetGiftPos, true);
      this.carriedGift.body.setLinvel({ x: 0, y: 0, z: 0 }, true);
      this.carriedGift.body.setRotation(
        {
          x: this.mesh.quaternion.x,
          y: this.mesh.quaternion.y,
          z: this.mesh.quaternion.z,
          w: this.mesh.quaternion.w
        },
        true
      );
    }

    return { noiseGenerated: noiseLevel, moved: isMoving };
  }

  public syncNetworkState(data: {
    pos: { x: number; y: number; z: number };
    rotY: number;
    isCrouching: boolean;
    isHoldingGift: boolean;
  }) {
    this.body.setTranslation(data.pos, true);
    this.mesh.position.set(data.pos.x, data.pos.y - 0.45 * (data.isCrouching ? 0.65 : 1.0), data.pos.z);
    this.mesh.rotation.y = data.rotY;
    this.isCrouching = data.isCrouching;
    this.isHoldingGift = data.isHoldingGift;
    this.mesh.scale.y = data.isCrouching ? 0.65 : 1.0;
  }

  public setPosition(pos: THREE.Vector3) {
    this.body.setTranslation({ x: pos.x, y: pos.y, z: pos.z }, true);
    this.body.setLinvel({ x: 0, y: 0, z: 0 }, true);
    this.slipVelocity.set(0, 0);
    this.mesh.position.copy(pos);
  }
}
