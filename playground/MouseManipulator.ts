import * as THREE from 'three';
import RAPIER from '@dimforge/rapier3d-compat';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { ActiveRagdollSystem } from './ActiveRagdollSystem';

export class MouseManipulator {
  public isGrabbing: boolean = false;
  public grabbedPartName: string = '';
  public forceMultiplier: number = 18.0;
  public currentPullVector: THREE.Vector3 = new THREE.Vector3();
  public currentGrabWorldPos: THREE.Vector3 = new THREE.Vector3();

  private scene: THREE.Scene;
  private camera: THREE.PerspectiveCamera;
  private domElement: HTMLElement;
  private controls: OrbitControls;
  private world: RAPIER.World;
  private ragdoll: ActiveRagdollSystem;

  private grabbedBody: RAPIER.RigidBody | null = null;
  private grabDistance: number = 0;
  private targetDragPos: THREE.Vector3 = new THREE.Vector3();
  private prevTargetPos: THREE.Vector3 = new THREE.Vector3();
  private dragVelocity: THREE.Vector3 = new THREE.Vector3();

  // Визуализация пружины захвата мышью
  private dragLine: THREE.Line;
  private grabMarker: THREE.Mesh;

  private onGrabChange?: (partName: string | null) => void;

  constructor(
    scene: THREE.Scene,
    camera: THREE.PerspectiveCamera,
    domElement: HTMLElement,
    controls: OrbitControls,
    world: RAPIER.World,
    ragdoll: ActiveRagdollSystem,
    onGrabChange?: (partName: string | null) => void
  ) {
    this.scene = scene;
    this.camera = camera;
    this.domElement = domElement;
    this.controls = controls;
    this.world = world;
    this.ragdoll = ragdoll;
    this.onGrabChange = onGrabChange;

    // 3D линия пружины захвата
    const lineGeo = new THREE.BufferGeometry().setFromPoints([
      new THREE.Vector3(),
      new THREE.Vector3()
    ]);
    const lineMat = new THREE.LineBasicMaterial({ color: 0x38bdf8, linewidth: 2 });
    this.dragLine = new THREE.Line(lineGeo, lineMat);
    this.dragLine.visible = false;
    this.scene.add(this.dragLine);

    // Сфера точки захвата
    this.grabMarker = new THREE.Mesh(
      new THREE.SphereGeometry(0.055, 12, 12),
      new THREE.MeshBasicMaterial({ color: 0xfacc15 })
    );
    this.grabMarker.visible = false;
    this.scene.add(this.grabMarker);

    this.setupPointerEvents();
  }

  private getRayFromPointer(clientX: number, clientY: number): THREE.Ray {
    const rect = this.domElement.getBoundingClientRect();
    const ndcX = ((clientX - rect.left) / rect.width) * 2 - 1;
    const ndcY = -((clientY - rect.top) / rect.height) * 2 + 1;

    const raycaster = new THREE.Raycaster();
    raycaster.setFromCamera(new THREE.Vector2(ndcX, ndcY), this.camera);
    return raycaster.ray;
  }

  private setupPointerEvents() {
    this.domElement.addEventListener('pointerdown', (e) => {
      if (e.button !== 0) return; // Только ЛКМ для захвата конечностей

      const threeRay = this.getRayFromPointer(e.clientX, e.clientY);
      const rapierRay = new RAPIER.Ray(
        { x: threeRay.origin.x, y: threeRay.origin.y, z: threeRay.origin.z },
        { x: threeRay.direction.x, y: threeRay.direction.y, z: threeRay.direction.z }
      );

      const hit = this.world.castRay(rapierRay, 40.0, true);
      if (hit) {
        const collider = hit.collider;
        const parentBody = collider.parent();
        if (parentBody && parentBody.isDynamic()) {
          this.isGrabbing = true;
          this.grabbedBody = parentBody;
          this.grabDistance = hit.timeOfImpact;

          const limb = this.ragdoll.colliderToLimb.get(collider.handle);
          this.grabbedPartName = limb ? limb.name : 'Dynamic Prop Box';

          this.targetDragPos
            .copy(threeRay.origin)
            .addScaledVector(threeRay.direction, this.grabDistance);
          this.prevTargetPos.copy(this.targetDragPos);
          this.dragVelocity.set(0, 0, 0);

          this.controls.enabled = false;
          this.dragLine.visible = true;
          this.grabMarker.visible = true;

          if (this.onGrabChange) this.onGrabChange(this.grabbedPartName);
        }
      }
    });

    window.addEventListener('pointermove', (e) => {
      if (!this.isGrabbing || !this.grabbedBody) return;

      const threeRay = this.getRayFromPointer(e.clientX, e.clientY);
      this.prevTargetPos.copy(this.targetDragPos);
      this.targetDragPos
        .copy(threeRay.origin)
        .addScaledVector(threeRay.direction, this.grabDistance);

      // Не даем утащить сквозь пол
      this.targetDragPos.y = Math.max(0.08, this.targetDragPos.y);
    });

    window.addEventListener('pointerup', () => {
      if (!this.isGrabbing) return;

      // Если мышь резко дернули перед отпусканием — бросаем тело по инерции (Throw on release)
      if (this.grabbedBody && this.dragVelocity.length() > 1.5) {
        const throwImpulse = this.dragVelocity
          .clone()
          .multiplyScalar(0.18 * (this.forceMultiplier / 15.0));
        this.grabbedBody.applyImpulse(
          { x: throwImpulse.x, y: throwImpulse.y, z: throwImpulse.z },
          true
        );
      }

      this.isGrabbing = false;
      this.grabbedBody = null;
      this.grabbedPartName = '';
      this.controls.enabled = true;
      this.dragLine.visible = false;
      this.grabMarker.visible = false;
      this.currentPullVector.set(0, 0, 0);

      if (this.onGrabChange) this.onGrabChange(null);
    });
  }

  public update(delta: number) {
    if (!this.isGrabbing || !this.grabbedBody) return;

    const bodyPos = this.grabbedBody.translation();
    const bodyVel = this.grabbedBody.linvel();
    this.currentGrabWorldPos.set(bodyPos.x, bodyPos.y, bodyPos.z);

    // Оценка скорости курсора для броска
    if (delta > 0.001) {
      const instantVel = this.targetDragPos.clone().sub(this.prevTargetPos).divideScalar(delta);
      this.dragVelocity.lerp(instantVel, 0.35);
    }

    // Пружинно-демпферная сила притяжения выбранной части тела к курсору
    const diff = this.targetDragPos.clone().sub(this.currentGrabWorldPos);
    const springK = this.forceMultiplier * 4.2;
    const damperC = 5.5;

    const fx = (diff.x * springK - bodyVel.x * damperC) * delta;
    const fy = (diff.y * springK - bodyVel.y * damperC) * delta;
    const fz = (diff.z * springK - bodyVel.z * damperC) * delta;

    this.grabbedBody.applyImpulse({ x: fx, y: fy, z: fz }, true);
    this.currentPullVector.set(fx * 25, fy * 25, fz * 25);

    // Обновляем 3D линию и маркер захвата
    const positions = this.dragLine.geometry.attributes.position.array as Float32Array;
    positions[0] = bodyPos.x;
    positions[1] = bodyPos.y;
    positions[2] = bodyPos.z;
    positions[3] = this.targetDragPos.x;
    positions[4] = this.targetDragPos.y;
    positions[5] = this.targetDragPos.z;
    this.dragLine.geometry.attributes.position.needsUpdate = true;

    this.grabMarker.position.copy(this.currentGrabWorldPos);
  }
}
