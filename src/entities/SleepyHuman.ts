import * as THREE from 'three';
import { SoundEngine } from '../audio/SoundEngine';
import { applyCartoonOutline, createPeakMaterial } from '../rendering/ToonStyle';
import { loadPeakModel } from '../loaders/ModelLoader';


// Общие геометрии и материалы (кэш)
const geoCache = new Map<string, THREE.BufferGeometry>();
function getGeo(key: string, make: () => THREE.BufferGeometry): THREE.BufferGeometry {
  let g = geoCache.get(key);
  if (!g) {
    g = make();
    geoCache.set(key, g);
  }
  return g;
}
const sphereGeo = (r: number) => getGeo(`s${r}`, () => new THREE.SphereGeometry(r, 24, 18));
const torusGeo = (r: number, t: number) => getGeo(`t${r}_${t}`, () => new THREE.TorusGeometry(r, t, 10, 32));
const capsuleGeo = (r: number, l: number) => getGeo(`c${r}_${l}`, () => new THREE.CapsuleGeometry(r, l, 6, 12));
const coneGeo = (r: number, h: number) =>
  getGeo(`k${r}_${h}`, () => {
    const pts = [new THREE.Vector2(0.001, h / 2), new THREE.Vector2(r * 0.25, h * 0.25), new THREE.Vector2(r * 0.65, -h * 0.15), new THREE.Vector2(r, -h / 2)];
    return new THREE.LatheGeometry(new THREE.SplineCurve(pts).getPoints(16), 20);
  });

const matCache = new Map<string, THREE.MeshPhysicalMaterial>();
function getMat(
  c: number,
  o?: { emissive?: number; emissiveIntensity?: number; roughness?: number; metalness?: number; knitBump?: boolean }
): THREE.MeshPhysicalMaterial {
  const key = `${c}_${o?.emissive ?? ''}_${o?.emissiveIntensity ?? ''}_${o?.roughness ?? ''}_${o?.metalness ?? ''}_${o?.knitBump ?? ''}`;
  let m = matCache.get(key);
  if (!m) {
    m = createPeakMaterial(c, o);
    matCache.set(key, m);
  }
  return m;
}

export type HumanState = 'SLEEPING' | 'WAKING_UP' | 'WALKING' | 'RETURNING' | 'ALERT_SPOTTED';

export class SleepyHuman {
  public mesh: THREE.Group;
  public state: HumanState = 'SLEEPING';
  public flashlight: THREE.SpotLight;
  public flashlightTarget: THREE.Object3D;

  private soundEngine: SoundEngine;
  private bedPos: THREE.Vector3;
  private bathPos: THREE.Vector3;

  private stateTimer: number = 0;
  private snoreTimer: number = 0;
  private walkCycle: number = 0;

  // Части тела
  private leftLeg: THREE.Mesh;
  private rightLeg: THREE.Mesh;
  private phoneMesh: THREE.Mesh;

  constructor(
    scene: THREE.Scene,
    soundEngine: SoundEngine,
    bedPos: THREE.Vector3,
    bathPos: THREE.Vector3
  ) {
    this.soundEngine = soundEngine;
    this.bedPos = bedPos.clone();
    this.bathPos = bathPos.clone();

    this.mesh = new THREE.Group();

    // 1. Модель человека в пижаме
    const { bodyGroup, leftLeg, rightLeg, phone, spotLight, spotLightTarget } = this.buildModel();
    this.mesh = bodyGroup;
    this.leftLeg = leftLeg;
    this.rightLeg = rightLeg;
    this.phoneMesh = phone;
    this.flashlight = spotLight;
    this.flashlightTarget = spotLightTarget;

    // Начальная позиция: в кровати
    this.mesh.position.set(this.bedPos.x, 0.6, this.bedPos.z);
    this.mesh.rotation.x = -Math.PI / 2; // лежит
    this.flashlight.intensity = 0;

    applyCartoonOutline(this.mesh, 0.025);

    loadPeakModel('/models/human.glb', 2.25).then((custom) => {
      if (custom) {
        this.mesh.children.forEach((c) => {
          if (c !== this.flashlight) c.visible = false;
        });
        this.mesh.add(custom);
      }
    });

    scene.add(this.mesh);
    scene.add(this.flashlightTarget);
  }

  private buildModel() {
    const group = new THREE.Group();

    const M = (c: number, o?: { emissive?: number; emissiveIntensity?: number; roughness?: number; metalness?: number; knitBump?: boolean }) =>
      getMat(c, o);
    const robeMat = M(0x2f4f86); // Пижама
    const pantsMat = M(0x3b5f9c);
    const stripeMat = M(0xe8eefc);
    const ropeMat = M(0xc9a15b);
    const skinMat = M(0xf0b998, { knitBump: false, roughness: 0.65 });
    const cheekMat = M(0xe98a7d, { knitBump: false });
    const beardMat = M(0xf1efe9);
    const mustacheMat = M(0xe6e3dc);
    const capRedMat = M(0xc13a3a);
    const capWhiteMat = M(0xfdfaf2);
    const slipperMat = M(0xa5764b);
    const slipperFurMat = M(0xf5ead4);
    const eyeWhite = M(0xffffff, { knitBump: false, roughness: 0.4 });
    const pupilMat = M(0x1b1b22, { knitBump: false, roughness: 0.3 });
    const browMat = M(0xd9d6cf);
    const glassMat = M(0xffffff, { emissive: 0xfef9c3, emissiveIntensity: 0.6, knitBump: false });
    const frameMat = M(0x6b4a2b, { knitBump: false });

    const add = (geo: THREE.BufferGeometry, mat: THREE.Material, x: number, y: number, z: number, sx = 1, sy = 1, sz = 1, parent: THREE.Object3D = group) => {
      const m = new THREE.Mesh(geo, mat);
      m.position.set(x, y, z);
      m.scale.set(sx, sy, sz);
      parent.add(m);
      return m;
    };

    // 1. Круглое пижамное тело + пояс
    add(sphereGeo(0.5), robeMat, 0, 1.12, 0, 0.72, 0.92, 0.66);
    // Полоски на пижаме
    [0.95, 1.12, 1.29].forEach((y) => {
      const r = 0.5 * Math.sqrt(1 - Math.pow((y - 1.12) / (0.5 * 0.92), 2));
      const st = add(torusGeo(1, 0.012), stripeMat, 0, y, 0, r * 0.72 * 1.01, r * 0.72 * 1.01, 1);
      st.rotation.x = Math.PI / 2;
      st.scale.set(r * 0.72 * 1.02, r * 0.66 * 1.02, 1);
    });
    const belt = add(torusGeo(0.34, 0.035), ropeMat, 0, 0.9, 0.01, 1.0, 0.92, 1);
    belt.rotation.x = Math.PI / 2;

    // 2. Голова
    add(sphereGeo(0.27), skinMat, 0, 1.82, 0, 1, 0.97, 0.95);
    // Уши
    [-1, 1].forEach((sd) => add(sphereGeo(0.06), skinMat, sd * 0.26, 1.8, 0, 0.6, 1, 0.9));
    // Румяные щёки
    [-1, 1].forEach((sd) => add(sphereGeo(0.05), cheekMat, sd * 0.15, 1.73, 0.2, 1, 0.7, 0.4));
    // Нос
    add(sphereGeo(0.075), skinMat, 0, 1.78, 0.26, 1, 0.9, 1);

    // Глаза (прищуренные, сонные) + очки + брови
    [-1, 1].forEach((sd) => {
      add(sphereGeo(0.045), eyeWhite, sd * 0.09, 1.87, 0.22, 1, 0.8, 0.5);
      add(sphereGeo(0.022), pupilMat, sd * 0.09, 1.865, 0.25, 1, 1, 0.6);
      const rim = add(torusGeo(0.07, 0.01), frameMat, sd * 0.09, 1.87, 0.235);
      void rim;
      const lens = add(sphereGeo(0.065), glassMat, sd * 0.09, 1.87, 0.225, 1, 1, 0.08);
      lens.renderOrder = 1;
      // Нахмуренная густая бровь
      const brow = add(capsuleGeo(0.018, 0.07), browMat, sd * 0.095, 1.96, 0.225);
      brow.rotation.z = Math.PI / 2 + sd * 0.35;
    });
    const bridge = add(capsuleGeo(0.008, 0.03), frameMat, 0, 1.875, 0.235);
    bridge.rotation.z = Math.PI / 2;

    // Пышные усы (две округлые половины)
    [-1, 1].forEach((sd) => {
      const mu = add(sphereGeo(0.09), mustacheMat, sd * 0.085, 1.71, 0.255, 1.35, 0.7, 0.7);
      mu.rotation.z = -sd * 0.25;
      add(sphereGeo(0.04), mustacheMat, sd * 0.17, 1.69, 0.24, 1, 0.9, 0.8);
    });
    // Борода
    add(sphereGeo(0.2), beardMat, 0, 1.6, 0.1, 1.0, 0.85, 0.8);

    // Ночной колпак с помпоном (положение/форма — изогнутый конус)
    const capBase = add(sphereGeo(0.29), capRedMat, 0, 1.95, -0.02, 1, 0.55, 1);
    void capBase;
    const capBand = add(torusGeo(0.265, 0.045), capWhiteMat, 0, 1.97, 0.0);
    capBand.rotation.x = Math.PI / 2 - 0.12;
    const capCone = add(coneGeo(0.22, 0.5), capRedMat, 0, 2.2, -0.1);
    capCone.rotation.x = -0.45;
    const capTip = add(sphereGeo(0.07), capWhiteMat, 0, 2.4, -0.26);
    void capTip;

    // 3. Ноги (пижамные штаны) с пивотом в бедре + пушистые тапки
    const legGeo = getGeo('leg', () => {
      const g = new THREE.CapsuleGeometry(0.085, 0.45, 6, 12);
      g.translate(0, -0.33, 0);
      return g;
    });
    const mkLeg = (x: number) => {
      const leg = new THREE.Mesh(legGeo, pantsMat);
      leg.position.set(x, 0.68, 0);
      group.add(leg);
      const slipper = add(sphereGeo(0.12), slipperMat, 0, -0.62, 0.06, 1.0, 0.6, 1.6, leg);
      add(sphereGeo(0.07), slipperFurMat, 0, -0.58, 0.1, 1.2, 0.7, 1.3, leg);
      void slipper;
      return leg;
    };
    const leftLeg = mkLeg(-0.15);
    const rightLeg = mkLeg(0.15);

    // Рука с телефоном
    const arm = add(capsuleGeo(0.075, 0.38), robeMat, 0.38, 1.25, 0.2);
    arm.rotation.x = -Math.PI / 3;
    add(sphereGeo(0.075), skinMat, 0.4, 1.34, 0.4);
    const other = add(capsuleGeo(0.07, 0.38), robeMat, -0.38, 1.1, 0.02);
    other.rotation.z = -0.25;
    add(sphereGeo(0.07), skinMat, -0.43, 0.9, 0.02);

    const phone = new THREE.Mesh(
      new THREE.BoxGeometry(0.1, 0.2, 0.04),
      M(0x1e293b, { emissive: 0xffffff, emissiveIntensity: 1.0, knitBump: false })
    );
    phone.position.set(0.42, 1.35, 0.45);
    group.add(phone);

    const spotLight = new THREE.SpotLight(0xfff7ed, 0, 9, Math.PI / 5, 0.3, 1.2);
    spotLight.position.set(0.42, 1.35, 0.45);
    spotLight.castShadow = true;
    group.add(spotLight);

    const spotLightTarget = new THREE.Object3D();
    spotLightTarget.position.set(0.42, 0, 4);
    spotLight.target = spotLightTarget;

    return {
      bodyGroup: group,
      leftLeg,
      rightLeg,
      phone,
      spotLight,
      spotLightTarget
    };
  }

  public setVisible(visible: boolean) {
    this.mesh.visible = visible;
    if (!visible) {
      this.flashlight.intensity = 0;
    }
  }

  public update(delta: number, currentNoise: number): { elfSpotted: boolean; stateName: string } {
    let elfSpotted = false;
    this.stateTimer += delta;

    switch (this.state) {
      case 'SLEEPING':
        // Периодический храп
        this.snoreTimer += delta;
        if (this.snoreTimer > 4.5) {
          this.snoreTimer = 0;
          this.soundEngine.playSnore();
        }

        // Пробуждение: либо от громкого шума (> 45 dB), либо каждые 25 сек
        if (currentNoise > 48 || this.stateTimer > 24) {
          this.state = 'WAKING_UP';
          this.stateTimer = 0;
          this.soundEngine.playDoorCreak();
        }
        break;

      case 'WAKING_UP':
        // Человек встает, включает телефон
        this.mesh.rotation.x = THREE.MathUtils.lerp(this.mesh.rotation.x, 0, 0.08);
        this.flashlight.intensity = THREE.MathUtils.lerp(this.flashlight.intensity, 3.5, 0.1);

        if (this.stateTimer > 2.0) {
          this.state = 'WALKING';
          this.stateTimer = 0;
        }
        break;

      case 'WALKING':
        // Идет в сторону ванной сонной походкой
        this.flashlight.intensity = 3.5;
        this.moveToward(this.bathPos, delta, 1.2);

        const distToBath = Math.hypot(
          this.mesh.position.x - this.bathPos.x,
          this.mesh.position.z - this.bathPos.z
        );
        if (distToBath < 1.0 || this.stateTimer > 14) {
          this.state = 'RETURNING';
          this.stateTimer = 0;
        }
        break;

      case 'RETURNING':
        // Идет обратно в кровать
        this.moveToward(this.bedPos, delta, 1.2);

        const distToBed = Math.hypot(
          this.mesh.position.x - this.bedPos.x,
          this.mesh.position.z - this.bedPos.z
        );
        if (distToBed < 0.8 || this.stateTimer > 14) {
          this.state = 'SLEEPING';
          this.stateTimer = 0;
          this.mesh.rotation.x = -Math.PI / 2;
          this.flashlight.intensity = 0;
        }
        break;

      case 'ALERT_SPOTTED':
        // Заметил эльфа!
        this.flashlight.intensity = 6.0;
        break;
    }

    // Обновление цели фонарика
    const forward = new THREE.Vector3(0, 0, 1).applyQuaternion(this.mesh.quaternion);
    this.flashlightTarget.position.copy(this.mesh.position).add(forward.multiplyScalar(4)).setY(0);

    return { elfSpotted, stateName: this.state };
  }

  private moveToward(target: THREE.Vector3, delta: number, speed: number) {
    const dir = new THREE.Vector3(target.x - this.mesh.position.x, 0, target.z - this.mesh.position.z);
    if (dir.length() > 0.1) {
      dir.normalize();
      this.mesh.position.addScaledVector(dir, speed * delta);

      const targetAngle = Math.atan2(dir.x, dir.z);
      this.mesh.rotation.y = THREE.MathUtils.lerp(this.mesh.rotation.y, targetAngle, 0.1);

      // Анимация ног
      this.walkCycle += delta * 5;
      this.leftLeg.rotation.x = Math.sin(this.walkCycle) * 0.4;
      this.rightLeg.rotation.x = -Math.sin(this.walkCycle) * 0.4;
    }
  }

  // Проверка попадания эльфа в конус света фонарика
  public checkVisionCone(elfPos: THREE.Vector3, isHidden: boolean): boolean {
    if (this.state === 'SLEEPING' || isHidden) {
      return false;
    }

    const eyePos = this.mesh.position.clone().setY(1.5);
    const toElf = new THREE.Vector3().subVectors(elfPos, eyePos);
    const dist = toElf.length();

    if (dist > 7.5) return false;

    // Вектор взгляда
    const lookDir = new THREE.Vector3(0, 0, 1).applyQuaternion(this.mesh.quaternion);
    toElf.normalize();

    const dot = lookDir.dot(toElf);
    // Косинус половины угла обзора фонарика (~35 градусов -> cos ~ 0.81)
    if (dot > 0.78) {
      this.state = 'ALERT_SPOTTED';
      this.soundEngine.playHorrorStinger();
      return true;
    }

    return false;
  }
}
