import * as THREE from 'three';
import RAPIER from '@dimforge/rapier3d-compat';
import { BuiltCharacterAsset, LimbRigSpec } from './ProceduralRigs';

export type RagdollPartName =
  | 'Pelvis'
  | 'Torso'
  | 'Head'
  | 'UpperArm_L'
  | 'LowerArm_L'
  | 'UpperArm_R'
  | 'LowerArm_R'
  | 'UpperLeg_L'
  | 'LowerLeg_L'
  | 'UpperLeg_R'
  | 'LowerLeg_R';

export interface RagdollLimb {
  name: RagdollPartName;
  body: RAPIER.RigidBody;
  collider: RAPIER.Collider;
  visualGroup?: THREE.Group;
  bone?: THREE.Bone;
  localOffsetFromBone: THREE.Vector3;
  initialLocalPos: THREE.Vector3;
  initialBoneQuat: THREE.Quaternion;
  mass: number;
}

export interface JointConnectionInfo {
  parentLimb: RagdollLimb;
  childLimb: RagdollLimb;
  joint: RAPIER.RevoluteImpulseJoint;
  parentAnchor: THREE.Vector3;
  childAnchor: THREE.Vector3;
  limits: [number, number];
}

export type MovementState = 'IDLE' | 'WALKING' | 'RUNNING' | 'STUMBLING' | 'RAGDOLL' | 'GETTING_UP' | 'STATIC_BODY';
export type BalanceState = 'BALANCED' | 'UNSTABLE' | 'FALLEN';

export class ActiveRagdollSystem {
  public world: RAPIER.World;
  public limbs: Map<RagdollPartName, RagdollLimb> = new Map();
  public colliderToLimb: Map<number, RagdollLimb> = new Map();
  public joints: JointConnectionInfo[] = [];

  public isFullRagdollAvailable: boolean = false;
  public mode: 'ACTIVE' | 'RAGDOLL' = 'ACTIVE';
  public physicsEnabled: boolean = true;

  public movementState: MovementState = 'IDLE';
  public balanceState: BalanceState = 'BALANCED';
  public balanceQuality: number = 100;

  // Настраиваемые физические параметры из UI
  public massScale: number = 1.0;
  public jointStiffness: number = 80.0;
  public jointDamping: number = 10.0;
  public balanceStrength: number = 85.0;
  public movementForce: number = 42.0;
  public friction: number = 0.6;
  public restitution: number = 0.05;

  private rootGroup: THREE.Group | null = null;
  private singleBody: RAPIER.RigidBody | null = null;
  private singleCollider: RAPIER.Collider | null = null;
  private walkPhase: number = 0;
  private recoveryTimer: number = 0;
  private fallenTimer: number = 0;
  private targetFacingYaw: number = 0;
  private standingPelvisHeight: number = 0.44;

  // Векторы телеметрии для DebugVisualizer
  public centerOfMass: THREE.Vector3 = new THREE.Vector3();
  public mainVelocity: THREE.Vector3 = new THREE.Vector3();
  public lastAppliedForce: THREE.Vector3 = new THREE.Vector3();

  // Битовая маска: все сегменты рэгдолла в группе 2, сталкиваются только с группой 1 (пол, лестница, кубы), но НЕ друг с другом!
  private static readonly RAGDOLL_COLLISION_GROUPS = (0x0002 << 16) | 0x0001;

  constructor(world: RAPIER.World) {
    this.world = world;
  }

  public clear() {
    for (const j of this.joints) {
      this.world.removeImpulseJoint(j.joint, true);
    }
    this.joints = [];

    this.limbs.forEach((limb) => {
      this.world.removeRigidBody(limb.body);
    });
    this.limbs.clear();
    this.colliderToLimb.clear();

    if (this.singleBody) {
      this.world.removeRigidBody(this.singleBody);
      this.singleBody = null;
      this.singleCollider = null;
    }
  }

  /**
   * Строит 11-частный физический Active Ragdoll с шарнирными моторами RevoluteImpulseJoint
   * и точными общими точками суставов (без единого разрыва между конечностями!).
   */
  public buildFromAsset(
    asset: BuiltCharacterAsset,
    spawnPos: THREE.Vector3 = new THREE.Vector3(0, 0.02, 0)
  ) {
    this.clear();
    this.rootGroup = asset.root;
    this.targetFacingYaw = 0;
    this.walkPhase = 0;

    // Если модель имеет готовые точные спецификации конечностей (Heist Elf, Grumpy Grandpa)
    let specs: LimbRigSpec[] = asset.limbSpecs;

    // Если загружен внешний GLB со скелетом — генерируем спецификации из его костей
    if (specs.length === 0 && asset.hasHumanoidRig) {
      specs = this.buildSpecsFromImportedBones(asset.root, asset.bones);
    }

    if (specs.length < 6) {
      this.buildSingleRigidBodyFallback(asset.root, spawnPos);
      return;
    }

    this.isFullRagdollAvailable = true;
    this.movementState = 'IDLE';
    this.balanceState = 'BALANCED';
    this.mode = 'ACTIVE';

    asset.root.position.set(0, 0, 0);
    asset.root.rotation.set(0, 0, 0);
    asset.root.updateMatrixWorld(true);

    // 1. Создаем физические тела (RigidBodies + Colliders) для каждого сегмента
    for (const spec of specs) {
      const worldPos = spec.centerWorldPos.clone().add(spawnPos);
      if (spec.name === 'Pelvis') {
        this.standingPelvisHeight = spec.centerWorldPos.y;
      }

      const bodyDesc = RAPIER.RigidBodyDesc.dynamic()
        .setTranslation(worldPos.x, worldPos.y, worldPos.z)
        .setLinearDamping(1.8)
        .setAngularDamping(4.5);

      const body = this.world.createRigidBody(bodyDesc);

      const colDesc =
        spec.name === 'Head'
          ? RAPIER.ColliderDesc.ball(spec.radius)
          : RAPIER.ColliderDesc.capsule(spec.halfHeight, spec.radius);

      colDesc
        .setMass(spec.mass * this.massScale)
        .setFriction(this.friction)
        .setRestitution(this.restitution)
        .setCollisionGroups(ActiveRagdollSystem.RAGDOLL_COLLISION_GROUPS);

      const collider = this.world.createCollider(colDesc, body);

      const limb: RagdollLimb = {
        name: spec.name,
        body,
        collider,
        visualGroup: spec.visualGroup,
        bone: spec.bone,
        localOffsetFromBone: new THREE.Vector3(),
        initialLocalPos: spec.centerWorldPos.clone(),
        initialBoneQuat: spec.bone ? spec.bone.quaternion.clone() : new THREE.Quaternion(),
        mass: spec.mass
      };

      this.limbs.set(spec.name, limb);
      this.colliderToLimb.set(collider.handle, limb);
    }

    // 2. Соединяем сегменты шарнирными суставами RevoluteImpulseJoint с точными общими якорями!
    for (const spec of specs) {
      if (!spec.parentName || !spec.jointWorldPivot) continue;
      const parentLimb = this.limbs.get(spec.parentName);
      const childLimb = this.limbs.get(spec.name);
      if (!parentLimb || !childLimb) continue;

      // Якорь в локальных координатах родителя и ребенка вычисляется из ОДНОЙ И ТОЙ ЖЕ мировой точки сустава:
      // Поэтому parentCenter + parentAnchor === childCenter + childAnchor === jointWorldPivot!
      const parentAnchor = spec.jointWorldPivot.clone().sub(parentLimb.initialLocalPos);
      const childAnchor = spec.jointWorldPivot.clone().sub(childLimb.initialLocalPos);
      const axis = spec.jointAxis || { x: 1, y: 0, z: 0 };
      const limits: [number, number] = spec.jointLimits || [-0.8, 0.8];

      const jointParams = RAPIER.JointData.revolute(
        { x: parentAnchor.x, y: parentAnchor.y, z: parentAnchor.z },
        { x: childAnchor.x, y: childAnchor.y, z: childAnchor.z },
        axis
      );
      jointParams.limitsEnabled = true;
      jointParams.limits = limits;

      const joint = this.world.createImpulseJoint(
        jointParams,
        parentLimb.body,
        childLimb.body,
        true
      ) as RAPIER.RevoluteImpulseJoint;

      // Включаем встроенный решатель мотора Rapier (удерживает позу 0 рад со стабильной пружиной)
      joint.configureMotorPosition(0.0, this.jointStiffness * 3.0, this.jointDamping * 1.5);

      this.joints.push({
        parentLimb,
        childLimb,
        joint,
        parentAnchor,
        childAnchor,
        limits
      });
    }

    this.syncVisualsFromPhysics();
  }

  /**
   * Если импортирован внешний GLB со скелетом — автоматически строит LimbRigSpec[]
   */
  private buildSpecsFromImportedBones(
    root: THREE.Group,
    bones: Record<string, THREE.Bone>
  ): LimbRigSpec[] {
    const bbox = new THREE.Box3().setFromObject(root);
    const h = Math.max(0.6, bbox.max.y - bbox.min.y);
    const s = h / 1.1;

    const specs: LimbRigSpec[] = [];
    const addSpec = (
      name: RagdollPartName,
      center: [number, number, number],
      halfH: number,
      rad: number,
      mass: number,
      parentName?: RagdollPartName,
      pivot?: [number, number, number],
      limits?: [number, number]
    ) => {
      const matchedBone = Object.values(bones).find((b) =>
        b.name.toLowerCase().includes(name.toLowerCase().split('_')[0])
      );
      specs.push({
        name,
        visualGroup: new THREE.Group(),
        bone: matchedBone || new THREE.Bone(),
        centerWorldPos: new THREE.Vector3(center[0] * s, center[1] * s, center[2] * s),
        halfHeight: halfH * s,
        radius: rad * s,
        mass,
        parentName,
        jointWorldPivot: pivot ? new THREE.Vector3(pivot[0] * s, pivot[1] * s, pivot[2] * s) : undefined,
        jointAxis: { x: 1, y: 0, z: 0 },
        jointLimits: limits || [-0.6, 0.6]
      });
    };

    addSpec('Pelvis', [0, 0.44, 0], 0.04, 0.13, 5.0);
    addSpec('Torso', [0, 0.64, 0], 0.07, 0.14, 5.5, 'Pelvis', [0, 0.52, 0], [-0.25, 0.35]);
    addSpec('Head', [0, 0.98, 0], 0.02, 0.19, 2.0, 'Torso', [0, 0.78, 0], [-0.3, 0.3]);
    addSpec('UpperArm_L', [-0.22, 0.64, 0], 0.055, 0.05, 0.9, 'Torso', [-0.22, 0.73, 0], [-1.8, 1.3]);
    addSpec('LowerArm_L', [-0.22, 0.44, 0], 0.06, 0.05, 0.8, 'UpperArm_L', [-0.22, 0.55, 0], [-1.9, 0.1]);
    addSpec('UpperArm_R', [0.22, 0.64, 0], 0.055, 0.05, 0.9, 'Torso', [0.22, 0.73, 0], [-1.8, 1.3]);
    addSpec('LowerArm_R', [0.22, 0.44, 0], 0.06, 0.05, 0.8, 'UpperArm_R', [0.22, 0.55, 0], [-1.9, 0.1]);
    addSpec('UpperLeg_L', [-0.105, 0.28, 0], 0.055, 0.065, 2.2, 'Pelvis', [-0.105, 0.38, 0], [-1.0, 1.0]);
    addSpec('LowerLeg_L', [-0.105, 0.095, 0], 0.045, 0.062, 2.0, 'UpperLeg_L', [-0.105, 0.19, 0], [0.0, 1.6]);
    addSpec('UpperLeg_R', [0.105, 0.28, 0], 0.055, 0.065, 2.2, 'Pelvis', [0.105, 0.38, 0], [-1.0, 1.0]);
    addSpec('LowerLeg_R', [0.105, 0.095, 0], 0.045, 0.062, 2.0, 'UpperLeg_R', [0.105, 0.19, 0], [0.0, 1.6]);

    return specs;
  }

  private buildSingleRigidBodyFallback(root: THREE.Group, spawnPos: THREE.Vector3) {
    this.isFullRagdollAvailable = false;
    this.movementState = 'STATIC_BODY';
    this.balanceState = 'BALANCED';

    root.position.set(0, 0, 0);
    root.rotation.set(0, 0, 0);
    root.updateMatrixWorld(true);

    const box = new THREE.Box3().setFromObject(root);
    const size = new THREE.Vector3();
    box.getSize(size);

    const hx = Math.max(0.15, size.x * 0.42);
    const hy = Math.max(0.15, size.y * 0.48);
    const hz = Math.max(0.15, size.z * 0.42);

    const bodyDesc = RAPIER.RigidBodyDesc.dynamic()
      .setTranslation(spawnPos.x, spawnPos.y + hy + 0.04, spawnPos.z)
      .setLinearDamping(0.8)
      .setAngularDamping(1.5);

    this.singleBody = this.world.createRigidBody(bodyDesc);
    this.singleCollider = this.world.createCollider(
      RAPIER.ColliderDesc.cuboid(hx, hy, hz)
        .setMass(6.0 * this.massScale)
        .setFriction(this.friction)
        .setRestitution(this.restitution),
      this.singleBody
    );

    const virtualLimb: RagdollLimb = {
      name: 'Pelvis',
      body: this.singleBody,
      collider: this.singleCollider,
      localOffsetFromBone: new THREE.Vector3(0, -hy, 0),
      initialLocalPos: new THREE.Vector3(0, hy, 0),
      initialBoneQuat: new THREE.Quaternion(),
      mass: 6.0
    };
    this.limbs.set('Pelvis', virtualLimb);
    this.colliderToLimb.set(this.singleCollider.handle, virtualLimb);
  }

  public update(
    delta: number,
    moveInput: { x: number; z: number; sprint: boolean; jump: boolean },
    cameraYaw: number,
    isAnimationInspectorOverride: boolean = false
  ) {
    if (!this.physicsEnabled || !this.rootGroup) return;

    // 1. Одиночное тело (Кот / меш без скелета)
    if (!this.isFullRagdollAvailable && this.singleBody) {
      const t = this.singleBody.translation();
      const r = this.singleBody.rotation();
      const limb = this.limbs.get('Pelvis')!;
      const offset = limb.localOffsetFromBone.clone().applyQuaternion(new THREE.Quaternion(r.x, r.y, r.z, r.w));
      this.rootGroup.position.set(t.x + offset.x, t.y + offset.y, t.z + offset.z);
      this.rootGroup.quaternion.set(r.x, r.y, r.z, r.w);
      this.centerOfMass.set(t.x, t.y, t.z);
      const lv = this.singleBody.linvel();
      this.mainVelocity.set(lv.x, lv.y, lv.z);
      return;
    }

    const pelvis = this.limbs.get('Pelvis');
    const torso = this.limbs.get('Torso');
    if (!pelvis || !torso) return;

    // 2. Центр масс (Center of Mass) и скорость
    let totalMass = 0;
    this.centerOfMass.set(0, 0, 0);
    this.limbs.forEach((limb) => {
      const p = limb.body.translation();
      this.centerOfMass.x += p.x * limb.mass;
      this.centerOfMass.y += p.y * limb.mass;
      this.centerOfMass.z += p.z * limb.mass;
      totalMass += limb.mass;
    });
    if (totalMass > 0) this.centerOfMass.divideScalar(totalMass);

    const pVel = pelvis.body.linvel();
    this.mainVelocity.set(pVel.x, pVel.y, pVel.z);

    // 3. Оценка угла наклона таза/торса от вертикали
    const pRot = pelvis.body.rotation();
    const pelvisQuat = new THREE.Quaternion(pRot.x, pRot.y, pRot.z, pRot.w);
    const currentUp = new THREE.Vector3(0, 1, 0).applyQuaternion(pelvisQuat);
    const tiltAngleRad = Math.acos(THREE.MathUtils.clamp(currentUp.y, -1, 1));
    this.balanceQuality = Math.round(Math.max(0, Math.min(100, (1 - tiltAngleRad / 1.3) * 100)));

    // 4. Режим чистого Ragdoll (моторы ослаблены, персонаж свободно падает)
    if (this.mode === 'RAGDOLL') {
      this.movementState = 'RAGDOLL';
      this.balanceState = 'FALLEN';
      for (const j of this.joints) {
        j.joint.configureMotorPosition(0, 4.0, 1.5);
      }
      this.syncVisualsFromPhysics();
      return;
    }

    // 5. Проверка потери равновесия (если опрокинули сильнее 60 градусов)
    if (tiltAngleRad > 1.05 && this.movementState !== 'GETTING_UP') {
      this.balanceState = 'FALLEN';
      this.movementState = 'RAGDOLL';
      this.fallenTimer += delta;
      for (const j of this.joints) {
        j.joint.configureMotorPosition(0, 15.0, 3.0);
      }
      if (this.fallenTimer > 1.5) {
        this.standUp();
      }
      this.syncVisualsFromPhysics();
      return;
    } else if (tiltAngleRad > 0.38) {
      this.balanceState = 'UNSTABLE';
    } else {
      this.balanceState = 'BALANCED';
      this.fallenTimer = 0;
    }

    // 6. Восстановление после падения (Stand Up)
    let recoveryFactor = 1.0;
    if (this.movementState === 'GETTING_UP') {
      this.recoveryTimer += delta;
      recoveryFactor = Math.min(1.0, this.recoveryTimer / 0.7);
      if (this.recoveryTimer >= 0.75) {
        this.movementState = 'IDLE';
      }
    }

    // 7. Обработка ходьбы и бега (WASD)
    const isMoving = Math.hypot(moveInput.x, moveInput.z) > 0.05;
    if (isMoving && this.movementState !== 'GETTING_UP') {
      this.targetFacingYaw = cameraYaw + Math.atan2(moveInput.x, moveInput.z);
      this.movementState = moveInput.sprint ? 'RUNNING' : 'WALKING';
    } else if (this.movementState !== 'GETTING_UP') {
      this.movementState = this.balanceState === 'UNSTABLE' ? 'STUMBLING' : 'IDLE';
    }

    // 8. Пружинная поддержка таза над полом (Ride Height) и прыжок
    const pPos = pelvis.body.translation();
    const ray = new RAPIER.Ray(pPos, { x: 0, y: -1, z: 0 });
    const groundHit = this.world.castRay(
      ray,
      this.standingPelvisHeight + 0.35,
      true,
      undefined,
      ActiveRagdollSystem.RAGDOLL_COLLISION_GROUPS
    );

    if (groundHit !== null) {
      const currentHeight = groundHit.timeOfImpact;
      const heightError = this.standingPelvisHeight - currentHeight;

      if (heightError > -0.1) {
        const springK = (this.balanceStrength / 85.0) * 280.0 * recoveryFactor;
        const damperC = 32.0;
        const liftImpulse = Math.max(-20, Math.min(180, heightError * springK - pVel.y * damperC)) * delta;
        pelvis.body.applyImpulse({ x: 0, y: liftImpulse, z: 0 }, true);
      }

      if (moveInput.jump && currentHeight < this.standingPelvisHeight + 0.1) {
        pelvis.body.applyImpulse({ x: 0, y: 11.0, z: 0 }, true);
        torso.body.applyImpulse({ x: 0, y: 8.0, z: 0 }, true);
      }
    }

    // 9. Импульс движения вперед и наклон корпуса
    let leanForward = 0.03;
    if (isMoving && groundHit !== null) {
      const speedMult = moveInput.sprint ? 1.55 : 1.0;
      const fx = Math.sin(this.targetFacingYaw) * this.movementForce * speedMult * delta;
      const fz = Math.cos(this.targetFacingYaw) * this.movementForce * speedMult * delta;
      pelvis.body.applyImpulse({ x: fx * 0.6, y: 0, z: fz * 0.6 }, true);
      torso.body.applyImpulse({ x: fx * 0.4, y: 0, z: fz * 0.4 }, true);
      this.lastAppliedForce.set(fx * 15, 0, fz * 15);
      leanForward = moveInput.sprint ? 0.22 : 0.1;
    }

    // 10. Удержание вертикального баланса таза и торса (плавный возврат к вертикали с инерцией!)
    const targetPelvisQuat = new THREE.Quaternion().setFromEuler(
      new THREE.Euler(leanForward, this.targetFacingYaw, 0, 'YXZ')
    );
    const uprightRate = Math.min(1.0, (this.balanceStrength / 85.0) * 14.0 * recoveryFactor * delta);
    const nextPelvisQuat = pelvisQuat.clone().slerp(targetPelvisQuat, uprightRate);
    pelvis.body.setRotation(
      { x: nextPelvisQuat.x, y: nextPelvisQuat.y, z: nextPelvisQuat.z, w: nextPelvisQuat.w },
      true
    );

    // Гасим паразитное угловое вращение таза
    const pAng = pelvis.body.angvel();
    pelvis.body.setAngvel({ x: pAng.x * 0.75, y: pAng.y * 0.6, z: pAng.z * 0.75 }, true);

    // 11. Приводы встроенных шарнирных моторов Rapier (шагание ног, взмахи рук или следование клипу Animation Inspector!)
    if (isMoving) {
      this.walkPhase += delta * (moveInput.sprint ? 15 : 10);
    } else {
      this.walkPhase = THREE.MathUtils.lerp(this.walkPhase, 0, delta * 8);
    }

    const swing = isMoving ? Math.sin(this.walkPhase) * (moveInput.sprint ? 0.75 : 0.5) : 0;
    const motorK = this.jointStiffness * 3.2 * recoveryFactor;
    const motorD = this.jointDamping * 1.6;

    for (const j of this.joints) {
      const childName = j.childLimb.name;
      let targetAngle = 0.0;

      if (isAnimationInspectorOverride && j.childLimb.bone) {
        // Если в Animation Inspector запущен клип — физические моторы повторяют позу костей!
        const euler = new THREE.Euler().setFromQuaternion(j.childLimb.bone.quaternion, 'XYZ');
        targetAngle = euler.x;
      } else {
        if (childName === 'Torso') targetAngle = leanForward * 0.5;
        else if (childName === 'Head') targetAngle = -leanForward * 0.3;
        else if (childName === 'UpperLeg_L') targetAngle = swing;
        else if (childName === 'LowerLeg_L') targetAngle = Math.max(0.05, -swing * 0.85);
        else if (childName === 'UpperLeg_R') targetAngle = -swing;
        else if (childName === 'LowerLeg_R') targetAngle = Math.max(0.05, swing * 0.85);
        else if (childName === 'UpperArm_L') targetAngle = -swing * 0.8;
        else if (childName === 'LowerArm_L') targetAngle = -0.25 - Math.max(0, swing * 0.5);
        else if (childName === 'UpperArm_R') targetAngle = swing * 0.8;
        else if (childName === 'LowerArm_R') targetAngle = -0.25 - Math.max(0, -swing * 0.5);
      }

      j.joint.configureMotorPosition(targetAngle, motorK, motorD);
    }

    this.syncVisualsFromPhysics();
  }

  /**
   * Прямая 1:1 синхронизация каждого визуального сегмента с его физическим телом Rapier:
   * Ни одна рука, нога или голова математически НЕ МОЖЕТ оторваться от коллайдера!
   */
  public syncVisualsFromPhysics() {
    this.limbs.forEach((limb) => {
      const p = limb.body.translation();
      const r = limb.body.rotation();

      if (limb.visualGroup) {
        limb.visualGroup.position.set(p.x, p.y, p.z);
        limb.visualGroup.quaternion.set(r.x, r.y, r.z, r.w);
      }
    });
  }

  // --- ИНТЕРАКТИВНЫЕ ДЕЙСТВИЯ ИЗ ПАНЕЛИ УПРАВЛЕНИЯ ---

  public pushCharacter(forceMultiplier: number = 18, cameraForward?: THREE.Vector3) {
    const dir = cameraForward ? cameraForward.clone().setY(0.15).normalize() : new THREE.Vector3(0, 0.15, -1).normalize();
    const torso = this.limbs.get('Torso');
    const pelvis = this.limbs.get('Pelvis');
    const impulse = dir.multiplyScalar(forceMultiplier * 0.9);

    if (torso) torso.body.applyImpulse({ x: impulse.x, y: impulse.y, z: impulse.z }, true);
    if (pelvis) pelvis.body.applyImpulse({ x: impulse.x * 0.6, y: impulse.y * 0.5, z: impulse.z * 0.6 }, true);
    this.lastAppliedForce.copy(impulse).multiplyScalar(6);
  }

  public knockDown(forceMultiplier: number = 18) {
    const pelvis = this.limbs.get('Pelvis');
    const torso = this.limbs.get('Torso');
    const legL = this.limbs.get('LowerLeg_L');
    const legR = this.limbs.get('LowerLeg_R');

    // Наклоняем таз за порог равновесия, чтобы сработал честный переход в RAGDOLL
    if (pelvis) {
      const qKnock = new THREE.Quaternion().setFromEuler(new THREE.Euler(1.2, this.targetFacingYaw, 0.2));
      pelvis.body.setRotation({ x: qKnock.x, y: qKnock.y, z: qKnock.z, w: qKnock.w }, true);
    }
    if (torso) {
      torso.body.applyImpulse({ x: 0, y: -2.0, z: forceMultiplier * 0.6 }, true);
    }
    if (legL) legL.body.applyImpulse({ x: 0, y: 2.0, z: -forceMultiplier * 0.5 }, true);
    if (legR) legR.body.applyImpulse({ x: 0, y: 2.0, z: -forceMultiplier * 0.5 }, true);

    this.balanceState = 'FALLEN';
    this.movementState = 'RAGDOLL';
    this.fallenTimer = 0;
  }

  public throwCharacter(forceMultiplier: number = 18, cameraForward?: THREE.Vector3) {
    const dir = cameraForward ? cameraForward.clone().setY(0.75).normalize() : new THREE.Vector3(0.2, 0.8, -1).normalize();
    this.limbs.forEach((limb) => {
      const imp = dir.clone().multiplyScalar(forceMultiplier * 0.24 * limb.mass);
      limb.body.applyImpulse({ x: imp.x, y: imp.y, z: imp.z }, true);
    });
    this.lastAppliedForce.copy(dir).multiplyScalar(forceMultiplier * 8);
  }

  public standUp() {
    if (!this.isFullRagdollAvailable) {
      if (this.singleBody) {
        const p = this.singleBody.translation();
        this.singleBody.setTranslation({ x: p.x, y: p.y + 0.5, z: p.z }, true);
        this.singleBody.setRotation({ x: 0, y: 0, z: 0, w: 1 }, true);
      }
      return;
    }

    this.mode = 'ACTIVE';
    this.movementState = 'GETTING_UP';
    this.balanceState = 'UNSTABLE';
    this.recoveryTimer = 0;
    this.fallenTimer = 0;

    // Выравниваем таз и поднимаем все части тела относительно новой точки стояния
    const pelvis = this.limbs.get('Pelvis');
    if (pelvis) {
      const pPos = pelvis.body.translation();
      const basePos = new THREE.Vector3(pPos.x, 0.04, pPos.z);
      const uprightQ = new THREE.Quaternion().setFromEuler(new THREE.Euler(0, this.targetFacingYaw, 0));

      this.limbs.forEach((limb) => {
        const rotatedOffset = limb.initialLocalPos.clone().applyQuaternion(uprightQ);
        const target = basePos.clone().add(rotatedOffset);
        limb.body.setTranslation({ x: target.x, y: target.y, z: target.z }, true);
        limb.body.setRotation({ x: uprightQ.x, y: uprightQ.y, z: uprightQ.z, w: uprightQ.w }, true);
        limb.body.setLinvel({ x: 0, y: 1.5, z: 0 }, true);
        limb.body.setAngvel({ x: 0, y: 0, z: 0 }, true);
      });
    }
  }

  public resetToOrigin(spawnPos: THREE.Vector3 = new THREE.Vector3(0, 0.02, 0)) {
    this.mode = 'ACTIVE';
    this.movementState = this.isFullRagdollAvailable ? 'IDLE' : 'STATIC_BODY';
    this.balanceState = 'BALANCED';
    this.targetFacingYaw = 0;
    this.fallenTimer = 0;

    if (!this.isFullRagdollAvailable && this.singleBody) {
      const limb = this.limbs.get('Pelvis')!;
      this.singleBody.setTranslation(
        { x: spawnPos.x, y: spawnPos.y + limb.initialLocalPos.y, z: spawnPos.z },
        true
      );
      this.singleBody.setRotation({ x: 0, y: 0, z: 0, w: 1 }, true);
      this.singleBody.setLinvel({ x: 0, y: 0, z: 0 }, true);
      this.singleBody.setAngvel({ x: 0, y: 0, z: 0 }, true);
      return;
    }

    this.limbs.forEach((limb) => {
      const target = spawnPos.clone().add(limb.initialLocalPos);
      limb.body.setTranslation({ x: target.x, y: target.y, z: target.z }, true);
      limb.body.setRotation({ x: 0, y: 0, z: 0, w: 1 }, true);
      limb.body.setLinvel({ x: 0, y: 0, z: 0 }, true);
      limb.body.setAngvel({ x: 0, y: 0, z: 0 }, true);
      if (limb.bone) {
        limb.bone.quaternion.copy(limb.initialBoneQuat);
      }
    });

    this.syncVisualsFromPhysics();
  }

  public updateMaterialProperties(massScale: number, friction: number, restitution: number) {
    this.massScale = massScale;
    this.friction = friction;
    this.restitution = restitution;

    this.limbs.forEach((limb) => {
      limb.collider.setMass(limb.mass * massScale);
      limb.collider.setFriction(friction);
      limb.collider.setRestitution(restitution);
    });
  }
}
