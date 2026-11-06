import * as THREE from 'three';
import RAPIER from '@dimforge/rapier3d-compat';
import { PhysicsWorld } from '../physics/PhysicsWorld';
import { SoundEngine } from '../audio/SoundEngine';
import { applyCartoonOutline, createPeakMaterial } from '../rendering/ToonStyle';
import { RoundedBoxGeometry } from 'three/examples/jsm/geometries/RoundedBoxGeometry.js';
import { loadPeakModel } from '../loaders/ModelLoader';


const geoCache = new Map<string, THREE.BufferGeometry>();
function getGeo(key: string, make: () => THREE.BufferGeometry): THREE.BufferGeometry {
  let g = geoCache.get(key);
  if (!g) {
    g = make();
    geoCache.set(key, g);
  }
  return g;
}
const sphereGeo = (r: number) => getGeo(`s${r}`, () => new THREE.SphereGeometry(r, 20, 16));
const torusGeo = (r: number, t: number) => getGeo(`t${r}_${t}`, () => new THREE.TorusGeometry(r, t, 12, 32));
const capsuleGeo = (r: number, l: number) => getGeo(`c${r}_${l}`, () => new THREE.CapsuleGeometry(r, l, 6, 12));
const rbox = (w: number, h: number, d: number, r: number) =>
  getGeo(`r${w}_${h}_${d}_${r}`, () => new RoundedBoxGeometry(w, h, d, 3, r));

const matCache = new Map<string, THREE.MeshPhysicalMaterial>();
function getMat(
  c: number,
  o?: { emissive?: number; emissiveIntensity?: number; roughness?: number; metalness?: number; knitBump?: boolean }
): THREE.MeshPhysicalMaterial {
  const key = `${c}_${o?.emissive ?? ''}_${o?.roughness ?? ''}_${o?.metalness ?? ''}_${o?.knitBump ?? ''}`;
  let m = matCache.get(key);
  if (!m) {
    m = createPeakMaterial(c, o);
    matCache.set(key, m);
  }
  return m;
}
function add(
  parent: THREE.Object3D,
  geo: THREE.BufferGeometry,
  mat: THREE.Material,
  x: number,
  y: number,
  z: number,
  sx = 1,
  sy = 1,
  sz = 1
): THREE.Mesh {
  const m = new THREE.Mesh(geo, mat);
  m.position.set(x, y, z);
  m.scale.set(sx, sy, sz);
  parent.add(m);
  return m;
}

export type GiftType = 'SMALL' | 'NOISY' | 'HUGE';

export class GiftItem {
  public id: string;
  public type: GiftType;
  public label: string;
  public baseTipValue: number;
  public integrity: number = 100; // Целостность рождественской упаковки (0..100%)

  public mesh: THREE.Group;
  public body: RAPIER.RigidBody;
  public collider: RAPIER.Collider;

  public carriers: Set<number> = new Set(); // ID игроков, которые держат этот подарок в руках
  public isDelivered: boolean = false;
  public isLoadedInSleigh: boolean = false;
  public isInSack: boolean = false;

  private soundEngine: SoundEngine;
  private noisyTimer: number = 0;
  private damageCooldown: number = 0;
  private windupKeyMesh: THREE.Mesh | null = null;

  constructor(
    id: string,
    type: GiftType,
    pos: THREE.Vector3,
    scene: THREE.Scene,
    physics: PhysicsWorld,
    soundEngine: SoundEngine
  ) {
    this.id = id;
    this.type = type;
    this.soundEngine = soundEngine;
    this.mesh = new THREE.Group();

    const R = physics.getRapier();

    if (type === 'SMALL') {
      this.label = 'Маленький подарок в фетре 📦';
      this.baseTipValue = 45;

      const boxMat = getMat(0xc62828);
      const ribbonMat = getMat(0x1f9d55);
      const bellMat = getMat(0xfbbf24, { metalness: 0.8, roughness: 0.25, knitBump: false });

      const g = this.mesh;
      add(g, rbox(0.38, 0.34, 0.38, 0.05), boxMat, 0, -0.02, 0);
      add(g, rbox(0.42, 0.1, 0.42, 0.04), boxMat, 0, 0.17, 0);
      // Ленты крест-накрест
      add(g, rbox(0.1, 0.36, 0.395, 0.025), ribbonMat, 0, -0.01, 0);
      add(g, rbox(0.395, 0.36, 0.1, 0.025), ribbonMat, 0, -0.01, 0);
      add(g, rbox(0.1, 0.105, 0.435, 0.03), ribbonMat, 0, 0.17, 0);
      add(g, rbox(0.435, 0.105, 0.1, 0.03), ribbonMat, 0, 0.17, 0);
      // Пышный бант
      [-1, 1].forEach((sd) => {
        const loop = add(g, sphereGeo(0.075), ribbonMat, sd * 0.075, 0.27, 0, 1.0, 0.7, 0.55);
        loop.rotation.z = sd * -0.5;
      });
      add(g, sphereGeo(0.045), ribbonMat, 0, 0.25, 0);
      add(g, sphereGeo(0.04), bellMat, 0, 0.215, 0.07);

      this.body = physics.world.createRigidBody(
        R.RigidBodyDesc.dynamic().setTranslation(pos.x, pos.y, pos.z).setLinearDamping(2.0)
      );
      this.collider = physics.world.createCollider(
        R.ColliderDesc.cuboid(0.2, 0.2, 0.2).setMass(1.0),
        this.body
      );
    } else if (type === 'NOISY') {
      this.label = 'Заводная шумная шкатулка ⏰';
      this.baseTipValue = 75;

      const woodMat = getMat(0xc2742b);
      const stripeMat = getMat(0xdc2626);
      const silverBellMat = getMat(0xe2e8f0, { metalness: 0.85, roughness: 0.2, knitBump: false });
      const goldMat = getMat(0xf5b021, { metalness: 0.8, roughness: 0.25, knitBump: false });

      const g = this.mesh;
      add(g, rbox(0.44, 0.44, 0.44, 0.06), woodMat, 0, 0, 0);
      add(g, rbox(0.465, 0.12, 0.465, 0.045), stripeMat, 0, 0, 0);
      // Золотая кнопка-застёжка и бантик-узел
      add(g, sphereGeo(0.035), goldMat, 0, 0, 0.235);
      [-1, 1].forEach((sd) => {
        const bow = add(g, sphereGeo(0.06), stripeMat, sd * 0.05, 0.1, 0.23, 1, 0.7, 0.5);
        bow.rotation.z = sd * -0.5;
      });

      // Боковые громкие чашки-звонки будильника
      [-0.25, 0.25].forEach((bx) => {
        const cup = new THREE.Mesh(getGeo('bellcup', () => new THREE.SphereGeometry(0.09, 20, 14, 0, Math.PI * 2, 0, Math.PI * 0.65)), silverBellMat);
        cup.position.set(bx, 0.16, 0);
        cup.rotation.z = bx < 0 ? Math.PI * 0.35 : -Math.PI * 0.35;
        g.add(cup);
        add(g, sphereGeo(0.022), goldMat, bx * 1.1, 0.25, 0);
      });

      // Заводной ключик сверху
      const keyGroup = new THREE.Group();
      keyGroup.position.set(0, 0.32, 0);

      this.windupKeyMesh = new THREE.Mesh(torusGeo(0.1, 0.025), goldMat);
      this.windupKeyMesh.rotation.x = Math.PI / 2;
      keyGroup.add(this.windupKeyMesh);
      add(keyGroup, capsuleGeo(0.022, 0.1), goldMat, 0, -0.06, 0);

      g.add(keyGroup);

      this.body = physics.world.createRigidBody(
        R.RigidBodyDesc.dynamic().setTranslation(pos.x, pos.y, pos.z).setLinearDamping(2.0)
      );
      this.collider = physics.world.createCollider(
        R.ColliderDesc.cuboid(0.22, 0.22, 0.22).setMass(1.4),
        this.body
      );
    } else {
      // 3. ОГРОМНЫЙ НЕГАБАРИТНЫЙ ПОДАРОК ПО ФОРМЕ ПРЕДМЕТА:
      // ВЕЛОСИПЕД В РОЖДЕСТВЕНСКОЙ УПАКОВКЕ (больше самого эльфа, с колесами, рамой и широким рулем!)
      this.label = 'Огромный Велосипед в обёртке 🚲';
      this.baseTipValue = 160;

      const redWrapMat = getMat(0xd62828);
      const greenWrapMat = getMat(0x1f9d55);
      const goldRibbonMat = getMat(0xfbbf24, { roughness: 0.4, metalness: 0.3, knitBump: false });
      const knitBowMat = getMat(0xef4444);
      const darkMat = getMat(0x3a2a22);

      const bikeGroup = new THREE.Group();
      // Смещаем так, чтобы центр коллайдера совпадал с центром велосипеда
      bikeGroup.position.set(0, -0.15, 0);

      // Колёса в обёрточной бумаге
      [-0.56, 0.56].forEach((wx, idx) => {
        const wheelMat = idx === 0 ? redWrapMat : greenWrapMat;
        const tire = add(bikeGroup, torusGeo(0.32, 0.085), wheelMat, wx, 0, 0);
        tire.castShadow = true;
        // Спицы-ленты и втулка
        add(bikeGroup, sphereGeo(0.06), goldRibbonMat, wx, 0, 0);
        [0, 1, 2, 3].forEach((i) => {
          const sp = add(bikeGroup, capsuleGeo(0.012, 0.52), goldRibbonMat, wx, 0, 0);
          sp.rotation.z = (i * Math.PI) / 4;
        });
        // Шина-внутренний обод
        add(bikeGroup, torusGeo(0.32, 0.03), darkMat, wx, 0, 0.07);
        add(bikeGroup, torusGeo(0.32, 0.03), darkMat, wx, 0, -0.07);
      });

      // Рама
      const mainTube = add(bikeGroup, capsuleGeo(0.06, 0.95), redWrapMat, 0, 0.18, 0, 1, 1, 1);
      mainTube.rotation.z = Math.PI / 2;
      mainTube.castShadow = true;
      const downTube = add(bikeGroup, capsuleGeo(0.055, 0.6), greenWrapMat, 0.22, 0.05, 0);
      downTube.rotation.z = 0.65;
      const stay = add(bikeGroup, capsuleGeo(0.04, 0.55), greenWrapMat, -0.3, 0.05, 0);
      stay.rotation.z = -0.9;
      // Золотые ленты-бантики на раме
      [-0.25, 0.1].forEach((rx) => {
        const rb = add(bikeGroup, torusGeo(0.068, 0.018), goldRibbonMat, rx, 0.18, 0);
        rb.rotation.y = Math.PI / 2;
      });

      // Стойка седла и седло
      const seatPost = add(bikeGroup, capsuleGeo(0.045, 0.4), goldRibbonMat, -0.28, 0.4, 0);
      seatPost.rotation.z = 0.2;
      add(bikeGroup, sphereGeo(0.16), redWrapMat, -0.34, 0.66, 0, 1.2, 0.55, 0.85);

      // Вилка и широкий руль
      const fork = add(bikeGroup, capsuleGeo(0.05, 0.6), greenWrapMat, 0.46, 0.36, 0);
      fork.rotation.z = 0.22;
      const handlebar = add(bikeGroup, capsuleGeo(0.04, 0.66), goldRibbonMat, 0.38, 0.74, 0);
      handlebar.rotation.x = Math.PI / 2;
      handlebar.castShadow = true;
      [-0.38, 0.38].forEach((hz) => add(bikeGroup, sphereGeo(0.055), darkMat, 0.38, 0.74, hz, 1, 1, 1));

      // Огромный вязаный бант на руле
      add(bikeGroup, sphereGeo(0.09), knitBowMat, 0.42, 0.84, 0);
      [-1, 1].forEach((sd) => {
        const loop = add(bikeGroup, sphereGeo(0.13), knitBowMat, 0.42, 0.88, sd * 0.13, 0.55, 0.9, 1.0);
        loop.rotation.x = sd * -0.5;
      });
      [-1, 1].forEach((sd) => {
        const tail = add(bikeGroup, capsuleGeo(0.03, 0.14), knitBowMat, 0.42, 0.72, sd * 0.06);
        tail.rotation.x = sd * 0.5;
      });

      this.mesh.add(bikeGroup);

      // Вытянутый физический коллайдер по форме велосипеда (длина 1.65м, высота 1.05м, ширина по рулю 0.65м)
      this.body = physics.world.createRigidBody(
        R.RigidBodyDesc.dynamic()
          .setTranslation(pos.x, pos.y, pos.z)
          .setLinearDamping(3.2)
          .setAngularDamping(2.2)
      );
      this.collider = physics.world.createCollider(
        R.ColliderDesc.cuboid(0.82, 0.52, 0.34).setMass(14.0),
        this.body
      );
    }

    applyCartoonOutline(this.mesh, 0.028);

    // Автоподгрузка пользовательских 3D моделек из public/models/ если они есть
    const modelFile =
      type === 'SMALL'
        ? '/models/gift_small.glb'
        : type === 'NOISY'
        ? '/models/gift_noisy.glb'
        : '/models/gift_huge.glb';
    const targetH = type === 'HUGE' ? 1.25 : 0.45;
    loadPeakModel(modelFile, targetH).then((custom) => {
      if (custom) {
        this.mesh.children.forEach((c) => (c.visible = false));
        this.mesh.add(custom);
      }
    });

    this.mesh.position.copy(pos);
    scene.add(this.mesh);
  }

  public canFitInSack(): boolean {
    return this.type === 'SMALL' || this.type === 'NOISY';
  }

  public update(delta: number, gripProtectionLevel: number = 0): { noiseGenerated: number } {
    if (this.isInSack) {
      return { noiseGenerated: 0 };
    }

    let noise = 0;
    this.damageCooldown = Math.max(0, this.damageCooldown - delta);

    // Вращение заводного ключа у шумного подарка
    if (this.windupKeyMesh && this.type === 'NOISY') {
      this.windupKeyMesh.rotation.z += delta * 4;

      this.noisyTimer += delta;
      if (this.noisyTimer > 4.2) {
        this.noisyTimer = 0;
        this.soundEngine.playJingleBell();
        noise = 38;
      }
    }

    // Проверка удара подарка об пол/косяки (целостность упаковки)
    const vel = this.body.linvel();
    const speed = Math.hypot(vel.x, vel.y, vel.z);
    if (speed > 4.2 && this.damageCooldown <= 0 && this.carriers.size === 0) {
      this.damageCooldown = 0.8;
      const dmgReduction = Math.min(0.75, gripProtectionLevel * 0.15);
      const dmg = Math.round((speed - 3.5) * 4 * (1 - dmgReduction));
      if (dmg > 0) {
        this.integrity = Math.max(25, this.integrity - dmg);
        this.soundEngine.playThud(0.5);
        noise = Math.max(noise, 50);
      }
    }

    const pos = this.body.translation();
    const rot = this.body.rotation();
    this.mesh.position.set(pos.x, pos.y, pos.z);
    this.mesh.quaternion.set(rot.x, rot.y, rot.z, rot.w);

    return { noiseGenerated: noise };
  }

  public getCarrySpeedMultiplier(gripLevel: number = 0): number {
    const gripBonus = gripLevel * 0.12;
    if (this.type === 'SMALL') return Math.min(1.15, 1.0 + gripBonus * 0.5);
    if (this.type === 'NOISY') return Math.min(1.1, 0.92 + gripBonus * 0.5);

    // Для Огромного Велосипеда в обёртке:
    const count = this.carriers.size;
    if (count === 0) return 1.0;
    if (count === 1) return Math.min(0.85, 0.42 + gripBonus); // С прокачкой хвата даже 1 эльф тянет бодрее!
    if (count === 2) return Math.min(1.15, 0.88 + gripBonus);
    return 1.2;
  }

  public setPosition(pos: THREE.Vector3) {
    this.body.setTranslation({ x: pos.x, y: pos.y, z: pos.z }, true);
    this.body.setLinvel({ x: 0, y: 0, z: 0 }, true);
    this.mesh.position.copy(pos);
  }

  public setVisible(visible: boolean) {
    this.mesh.visible = visible && !this.isInSack;
  }
}
