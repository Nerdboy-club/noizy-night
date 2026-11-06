import * as THREE from 'three';
import RAPIER from '@dimforge/rapier3d-compat';
import { ActiveRagdollSystem } from './ActiveRagdollSystem';
import { MouseManipulator } from './MouseManipulator';

export class DebugVisualizer {
  public showCollisionShapes: boolean = false;
  public showJoints: boolean = false;
  public showCenterOfMass: boolean = false;
  public showVelocityVectors: boolean = false;
  public showForceVectors: boolean = false;

  private scene: THREE.Scene;
  private world: RAPIER.World;
  private ragdoll: ActiveRagdollSystem;
  private manipulator: MouseManipulator;

  // 1. Rapier Debug Render Lines (Collision shapes)
  private colliderLines: THREE.LineSegments;

  // 2. Joint connections
  private jointLines: THREE.LineSegments;
  private jointDotsGroup: THREE.Group;

  // 3. Center of Mass (CoM) sphere + ground projection line
  private comGroup: THREE.Group;
  private comSphere: THREE.Mesh;
  private comGroundRing: THREE.Mesh;
  private comPlumbLine: THREE.Line;

  // 4 & 5. Arrows for Velocity & Force
  private velocityArrow: THREE.ArrowHelper;
  private forceArrow: THREE.ArrowHelper;

  constructor(
    scene: THREE.Scene,
    world: RAPIER.World,
    ragdoll: ActiveRagdollSystem,
    manipulator: MouseManipulator
  ) {
    this.scene = scene;
    this.world = world;
    this.ragdoll = ragdoll;
    this.manipulator = manipulator;

    // 1. Сетка коллайдеров Rapier
    const colGeo = new THREE.BufferGeometry();
    const colMat = new THREE.LineBasicMaterial({ vertexColors: true, transparent: true, opacity: 0.85 });
    this.colliderLines = new THREE.LineSegments(colGeo, colMat);
    this.colliderLines.visible = false;
    this.scene.add(this.colliderLines);

    // 2. Линии суставов
    const jGeo = new THREE.BufferGeometry();
    const jMat = new THREE.LineBasicMaterial({ color: 0xf43f5e, depthTest: false });
    this.jointLines = new THREE.LineSegments(jGeo, jMat);
    this.jointLines.renderOrder = 999;
    this.jointLines.visible = false;
    this.scene.add(this.jointLines);

    this.jointDotsGroup = new THREE.Group();
    this.jointDotsGroup.visible = false;
    this.scene.add(this.jointDotsGroup);

    // 3. Центр масс (Center of Mass)
    this.comGroup = new THREE.Group();
    this.comGroup.visible = false;

    this.comSphere = new THREE.Mesh(
      new THREE.SphereGeometry(0.065, 16, 16),
      new THREE.MeshBasicMaterial({ color: 0x10b981, depthTest: false })
    );
    this.comSphere.renderOrder = 1000;
    this.comGroup.add(this.comSphere);

    this.comGroundRing = new THREE.Mesh(
      new THREE.RingGeometry(0.06, 0.095, 24),
      new THREE.MeshBasicMaterial({ color: 0x10b981, side: THREE.DoubleSide, depthTest: false })
    );
    this.comGroundRing.rotation.x = -Math.PI / 2;
    this.comGroundRing.position.y = 0.01;
    this.comGroup.add(this.comGroundRing);

    const plumbGeo = new THREE.BufferGeometry().setFromPoints([
      new THREE.Vector3(),
      new THREE.Vector3()
    ]);
    this.comPlumbLine = new THREE.Line(
      plumbGeo,
      new THREE.LineDashedMaterial({ color: 0x10b981, dashSize: 0.05, gapSize: 0.03, depthTest: false })
    );
    this.comGroup.add(this.comPlumbLine);
    this.scene.add(this.comGroup);

    // 4. Вектор скорости (Velocity)
    this.velocityArrow = new THREE.ArrowHelper(
      new THREE.Vector3(0, 0, 1),
      new THREE.Vector3(),
      0.5,
      0x06b6d4,
      0.14,
      0.08
    );
    this.velocityArrow.visible = false;
    this.scene.add(this.velocityArrow);

    // 5. Вектор силы (Force)
    this.forceArrow = new THREE.ArrowHelper(
      new THREE.Vector3(0, 1, 0),
      new THREE.Vector3(),
      0.5,
      0xf97316,
      0.16,
      0.09
    );
    this.forceArrow.visible = false;
    this.scene.add(this.forceArrow);
  }

  public update() {
    // 1. Отрисовка форм коллайдеров Rapier
    this.colliderLines.visible = this.showCollisionShapes;
    if (this.showCollisionShapes) {
      const { vertices, colors } = this.world.debugRender();
      this.colliderLines.geometry.setAttribute('position', new THREE.BufferAttribute(vertices, 3));
      this.colliderLines.geometry.setAttribute('color', new THREE.BufferAttribute(colors, 4));
    }

    // 2. Отрисовка соединений суставов (Joints)
    this.jointLines.visible = this.showJoints;
    this.jointDotsGroup.visible = this.showJoints;
    if (this.showJoints) {
      const pts: number[] = [];
      for (const j of this.ragdoll.joints) {
        const p1 = j.parentLimb.body.translation();
        const p2 = j.childLimb.body.translation();
        pts.push(p1.x, p1.y, p1.z, p2.x, p2.y, p2.z);
      }
      this.jointLines.geometry.setAttribute(
        'position',
        new THREE.Float32BufferAttribute(pts, 3)
      );
    }

    // 3. Центр масс (Center of Mass) и его проекция на пол
    this.comGroup.visible = this.showCenterOfMass;
    if (this.showCenterOfMass) {
      const com = this.ragdoll.centerOfMass;
      this.comSphere.position.copy(com);
      this.comGroundRing.position.set(com.x, 0.012, com.z);

      const pArr = this.comPlumbLine.geometry.attributes.position.array as Float32Array;
      pArr[0] = com.x;
      pArr[1] = com.y;
      pArr[2] = com.z;
      pArr[3] = com.x;
      pArr[4] = 0.012;
      pArr[5] = com.z;
      this.comPlumbLine.geometry.attributes.position.needsUpdate = true;
    }

    // 4. Вектор скорости (Velocity Vector)
    const vel = this.ragdoll.mainVelocity;
    const speed = vel.length();
    this.velocityArrow.visible = this.showVelocityVectors && speed > 0.08;
    if (this.velocityArrow.visible) {
      this.velocityArrow.position.copy(this.ragdoll.centerOfMass);
      this.velocityArrow.setDirection(vel.clone().normalize());
      this.velocityArrow.setLength(Math.min(2.5, speed * 0.45), 0.14, 0.08);
    }

    // 5. Вектор приложенной силы (Force Vector)
    const activeForce = this.manipulator.isGrabbing
      ? this.manipulator.currentPullVector
      : this.ragdoll.lastAppliedForce;
    const forceMag = activeForce.length();
    this.forceArrow.visible = this.showForceVectors && forceMag > 0.15;
    if (this.forceArrow.visible) {
      const origin = this.manipulator.isGrabbing
        ? this.manipulator.currentGrabWorldPos
        : this.ragdoll.centerOfMass;
      this.forceArrow.position.copy(origin);
      this.forceArrow.setDirection(activeForce.clone().normalize());
      this.forceArrow.setLength(Math.min(2.8, forceMag * 0.12), 0.16, 0.09);
    }
    // Плавное затухание вектора импульса
    this.ragdoll.lastAppliedForce.multiplyScalar(0.88);
  }

  /**
   * Подсчет числа активных контактов (коллизий) персонажа с миром и объектами
   */
  public countActiveCollisions(): number {
    let count = 0;
    this.ragdoll.limbs.forEach((limb) => {
      this.world.contactPairsWith(limb.collider, () => {
        count++;
      });
    });
    return count;
  }
}
