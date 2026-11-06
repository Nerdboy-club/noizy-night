import * as THREE from 'three';
import RAPIER from '@dimforge/rapier3d-compat';

export interface ArenaProp {
  mesh: THREE.Mesh;
  body: RAPIER.RigidBody;
  collider: RAPIER.Collider;
  initialPos: THREE.Vector3;
}

export class PlaygroundArena {
  public group: THREE.Group;
  public floorMesh!: THREE.Mesh;
  public gridHelper!: THREE.GridHelper;
  public props: ArenaProp[] = [];
  public staticColliders: RAPIER.Collider[] = [];

  private world: RAPIER.World;

  constructor(scene: THREE.Scene, world: RAPIER.World) {
    this.world = world;
    this.group = new THREE.Group();
    this.buildEnvironment();
    this.spawnMovableProps();
    scene.add(this.group);
  }

  private buildEnvironment() {
    // 1. Основная ровная поверхность (Flat Ground)
    const floorGeo = new THREE.BoxGeometry(36, 0.4, 36);
    const floorMat = new THREE.MeshStandardMaterial({
      color: 0x1e222b,
      roughness: 0.85,
      metalness: 0.08
    });
    this.floorMesh = new THREE.Mesh(floorGeo, floorMat);
    this.floorMesh.position.set(0, -0.2, 0);
    this.floorMesh.receiveShadow = true;
    this.group.add(this.floorMesh);

    const floorBody = this.world.createRigidBody(RAPIER.RigidBodyDesc.fixed().setTranslation(0, -0.2, 0));
    const floorCol = this.world.createCollider(
      RAPIER.ColliderDesc.cuboid(18, 0.2, 18).setFriction(0.8).setRestitution(0.05),
      floorBody
    );
    this.staticColliders.push(floorCol);

    // Сетка (Grid)
    this.gridHelper = new THREE.GridHelper(36, 36, 0x3b82f6, 0x2a303c);
    this.gridHelper.position.y = 0.004;
    this.group.add(this.gridHelper);

    // 2. Ледяная скользкая зона (Slippery Ice Surface — слева)
    const iceGeo = new THREE.BoxGeometry(6.5, 0.06, 8.0);
    const iceMat = new THREE.MeshStandardMaterial({
      color: 0x38bdf8,
      roughness: 0.06,
      metalness: 0.25,
      transparent: true,
      opacity: 0.78
    });
    const iceMesh = new THREE.Mesh(iceGeo, iceMat);
    iceMesh.position.set(-5.8, 0.03, 0);
    iceMesh.receiveShadow = true;
    this.group.add(iceMesh);

    const iceBody = this.world.createRigidBody(RAPIER.RigidBodyDesc.fixed().setTranslation(-5.8, 0.03, 0));
    this.world.createCollider(
      RAPIER.ColliderDesc.cuboid(3.25, 0.03, 4.0).setFriction(0.01).setRestitution(0.1),
      iceBody
    );

    // 3. Наклонная рампа (Inclined Ramp — сзади справа)
    const rampAngle = 0.32; // ~18 градусов
    const rampGeo = new THREE.BoxGeometry(3.2, 0.25, 5.5);
    const rampMat = new THREE.MeshStandardMaterial({ color: 0x334155, roughness: 0.7 });
    const rampMesh = new THREE.Mesh(rampGeo, rampMat);
    rampMesh.position.set(4.5, 0.78, -4.0);
    rampMesh.rotation.x = rampAngle;
    rampMesh.castShadow = true;
    rampMesh.receiveShadow = true;
    this.group.add(rampMesh);

    const qRamp = new THREE.Quaternion().setFromEuler(new THREE.Euler(rampAngle, 0, 0));
    const rampBody = this.world.createRigidBody(
      RAPIER.RigidBodyDesc.fixed()
        .setTranslation(4.5, 0.78, -4.0)
        .setRotation({ x: qRamp.x, y: qRamp.y, z: qRamp.z, w: qRamp.w })
    );
    const rampCol = this.world.createCollider(
      RAPIER.ColliderDesc.cuboid(1.6, 0.125, 2.75).setFriction(0.75),
      rampBody
    );
    this.staticColliders.push(rampCol);

    // Верхняя площадка за рампой
    const platGeo = new THREE.BoxGeometry(3.2, 1.65, 2.6);
    const platMesh = new THREE.Mesh(platGeo, rampMat);
    platMesh.position.set(4.5, 0.825, -7.8);
    platMesh.castShadow = true;
    platMesh.receiveShadow = true;
    this.group.add(platMesh);

    const platBody = this.world.createRigidBody(RAPIER.RigidBodyDesc.fixed().setTranslation(4.5, 0.825, -7.8));
    this.world.createCollider(RAPIER.ColliderDesc.cuboid(1.6, 0.825, 1.3), platBody);

    // 4. Лестница (Stairs — 6 ступенек сзади по центру)
    const stairMat = new THREE.MeshStandardMaterial({ color: 0x475569, roughness: 0.75 });
    for (let i = 0; i < 6; i++) {
      const stepH = 0.16;
      const stepD = 0.48;
      const h = (i + 1) * stepH;
      const z = -3.2 - i * stepD;

      const sMesh = new THREE.Mesh(new THREE.BoxGeometry(2.6, h, stepD), stairMat);
      sMesh.position.set(0, h / 2, z);
      sMesh.castShadow = true;
      sMesh.receiveShadow = true;
      this.group.add(sMesh);

      const sBody = this.world.createRigidBody(RAPIER.RigidBodyDesc.fixed().setTranslation(0, h / 2, z));
      this.world.createCollider(RAPIER.ColliderDesc.cuboid(1.3, h / 2, stepD / 2), sBody);
    }

    // 5. Низкий барьер для спотыкания / перешагивания (Low Obstacle)
    const hurdleMat = new THREE.MeshStandardMaterial({ color: 0xd97706, roughness: 0.6 });
    const hurdleMesh = new THREE.Mesh(new THREE.BoxGeometry(2.8, 0.22, 0.24), hurdleMat);
    hurdleMesh.position.set(0, 0.11, 2.4);
    hurdleMesh.castShadow = true;
    hurdleMesh.receiveShadow = true;
    this.group.add(hurdleMesh);

    const hurdleBody = this.world.createRigidBody(RAPIER.RigidBodyDesc.fixed().setTranslation(0, 0.11, 2.4));
    this.world.createCollider(RAPIER.ColliderDesc.cuboid(1.4, 0.11, 0.12), hurdleBody);

    // 6. Небольшие ступенчатые платформы справа (Small Platforms)
    const platformSpecs = [
      { pos: [4.2, 0.18, 1.5], size: [1.6, 0.36, 1.6], col: 0x3b82f6 },
      { pos: [4.2, 0.36, 3.4], size: [1.6, 0.72, 1.6], col: 0x6366f1 }
    ];
    platformSpecs.forEach((p) => {
      const m = new THREE.Mesh(
        new THREE.BoxGeometry(p.size[0], p.size[1], p.size[2]),
        new THREE.MeshStandardMaterial({ color: p.col, roughness: 0.65 })
      );
      m.position.set(p.pos[0], p.pos[1], p.pos[2]);
      m.castShadow = true;
      m.receiveShadow = true;
      this.group.add(m);

      const b = this.world.createRigidBody(
        RAPIER.RigidBodyDesc.fixed().setTranslation(p.pos[0], p.pos[1], p.pos[2])
      );
      this.world.createCollider(
        RAPIER.ColliderDesc.cuboid(p.size[0] / 2, p.size[1] / 2, p.size[2] / 2),
        b
      );
    });
  }

  private spawnMovableProps() {
    const boxConfigs = [
      // Пирамидка легких кубиков
      { pos: new THREE.Vector3(-1.8, 0.22, -1.5), size: 0.36, mass: 1.0, color: 0xef4444 },
      { pos: new THREE.Vector3(-1.35, 0.22, -1.5), size: 0.36, mass: 1.0, color: 0x10b981 },
      { pos: new THREE.Vector3(-1.58, 0.62, -1.5), size: 0.36, mass: 0.8, color: 0xf59e0b },
      // Средняя и тяжелая подарочные коробки
      { pos: new THREE.Vector3(1.8, 0.3, 0.6), size: 0.52, mass: 4.5, color: 0x8b5cf6 },
      { pos: new THREE.Vector3(2.4, 0.45, -0.8), size: 0.78, mass: 14.0, color: 0xd97706 },
      // Мелкие кубики на пути для проверки спотыкания
      { pos: new THREE.Vector3(0.35, 0.12, 1.2), size: 0.2, mass: 0.5, color: 0x38bdf8 },
      { pos: new THREE.Vector3(-0.35, 0.12, 1.35), size: 0.2, mass: 0.5, color: 0xf43f5e }
    ];

    boxConfigs.forEach((cfg) => {
      const mesh = new THREE.Mesh(
        new THREE.BoxGeometry(cfg.size, cfg.size, cfg.size),
        new THREE.MeshStandardMaterial({ color: cfg.color, roughness: 0.55 })
      );
      mesh.position.copy(cfg.pos);
      mesh.castShadow = true;
      mesh.receiveShadow = true;
      this.group.add(mesh);

      const body = this.world.createRigidBody(
        RAPIER.RigidBodyDesc.dynamic()
          .setTranslation(cfg.pos.x, cfg.pos.y, cfg.pos.z)
          .setLinearDamping(0.4)
          .setAngularDamping(0.4)
      );
      const half = cfg.size / 2;
      const collider = this.world.createCollider(
        RAPIER.ColliderDesc.cuboid(half, half, half)
          .setMass(cfg.mass)
          .setFriction(0.65)
          .setRestitution(0.15),
        body
      );

      this.props.push({ mesh, body, collider, initialPos: cfg.pos.clone() });
    });
  }

  public update() {
    for (const p of this.props) {
      const t = p.body.translation();
      const r = p.body.rotation();
      p.mesh.position.set(t.x, t.y, t.z);
      p.mesh.quaternion.set(r.x, r.y, r.z, r.w);
    }
  }

  public resetProps() {
    for (const p of this.props) {
      p.body.setTranslation(p.initialPos, true);
      p.body.setRotation({ x: 0, y: 0, z: 0, w: 1 }, true);
      p.body.setLinvel({ x: 0, y: 0, z: 0 }, true);
      p.body.setAngvel({ x: 0, y: 0, z: 0 }, true);
    }
  }

  public setFriction(friction: number) {
    for (const c of this.staticColliders) {
      c.setFriction(friction);
    }
    for (const p of this.props) {
      p.collider.setFriction(friction);
    }
  }

  public setRestitution(restitution: number) {
    for (const c of this.staticColliders) {
      c.setRestitution(restitution);
    }
    for (const p of this.props) {
      p.collider.setRestitution(restitution);
    }
  }
}
