import * as THREE from 'three';
import { RagdollPartName } from './ActiveRagdollSystem';

export interface LimbRigSpec {
  name: RagdollPartName;
  visualGroup: THREE.Group;
  bone: THREE.Bone;
  centerWorldPos: THREE.Vector3;     // Точный центр физического тела в стартовой позе
  halfHeight: number;                // Полувысота капсулы
  radius: number;                    // Радиус капсулы/сферы
  mass: number;
  parentName?: RagdollPartName;
  jointWorldPivot?: THREE.Vector3;   // Точка сустава в мировых координатах (единая для родителя и ребенка!)
  jointAxis?: { x: number; y: number; z: number };
  jointLimits?: [number, number];    // Анатомические лимиты угла в радианах
}

export interface BuiltCharacterAsset {
  root: THREE.Group;
  skeleton: THREE.Skeleton;
  bones: Record<string, THREE.Bone>;
  limbSpecs: LimbRigSpec[];
  animations: THREE.AnimationClip[];
  hasHumanoidRig: boolean;
}

function createKnitTexture(baseHex: number, stripeHex?: number): THREE.CanvasTexture {
  const canvas = document.createElement('canvas');
  canvas.width = 128;
  canvas.height = 128;
  const ctx = canvas.getContext('2d')!;

  const baseColor = new THREE.Color(baseHex);
  ctx.fillStyle = `#${baseColor.getHexString()}`;
  ctx.fillRect(0, 0, 128, 128);

  if (stripeHex !== undefined) {
    const sCol = new THREE.Color(stripeHex);
    ctx.fillStyle = `#${sCol.getHexString()}`;
    for (let y = 0; y < 128; y += 32) {
      ctx.fillRect(0, y, 128, 16);
    }
  }

  ctx.strokeStyle = 'rgba(0,0,0,0.18)';
  ctx.lineWidth = 2;
  for (let x = 0; x < 128; x += 8) {
    for (let y = 0; y < 128; y += 8) {
      ctx.beginPath();
      ctx.moveTo(x, y);
      ctx.lineTo(x + 4, y + 6);
      ctx.lineTo(x + 8, y);
      ctx.stroke();
    }
  }

  const tex = new THREE.CanvasTexture(canvas);
  tex.wrapS = THREE.RepeatWrapping;
  tex.wrapT = THREE.RepeatWrapping;
  tex.repeat.set(3, 3);
  return tex;
}

/**
 * 1. УТВЕРЖДЕННЫЙ ЭЛЬФ-ДОМУШНИК (Heist Elf)
 * Каждая из 11 частей тела имеет собственный центрированный visualGroup + общие точки суставов (jointWorldPivot),
 * поэтому конечности НИКОГДА не отрываются от тела и сгибаются строго в суставах!
 */
export function buildApprovedHeistElf(): BuiltCharacterAsset {
  const root = new THREE.Group();
  root.name = 'HeistElf_Approved';

  const redKnitMat = new THREE.MeshStandardMaterial({
    color: 0xc83228,
    map: createKnitTexture(0xc83228),
    roughness: 0.82
  });
  const greenSweaterMat = new THREE.MeshStandardMaterial({
    color: 0x3b6e38,
    map: createKnitTexture(0x3b6e38),
    roughness: 0.85
  });
  const scarfMat = new THREE.MeshStandardMaterial({
    map: createKnitTexture(0xd9382e, 0xf5f0e6),
    roughness: 0.85
  });
  const pantsMat = new THREE.MeshStandardMaterial({
    color: 0x262220,
    roughness: 0.9
  });
  const bootMat = new THREE.MeshStandardMaterial({
    color: 0x3b2618,
    roughness: 0.85
  });
  const skinMat = new THREE.MeshStandardMaterial({
    color: 0xf2bfa0,
    roughness: 0.65
  });
  const mittenMat = new THREE.MeshStandardMaterial({
    color: 0xc22b22,
    roughness: 0.75
  });
  const eyeYellowMat = new THREE.MeshStandardMaterial({
    color: 0xffd040,
    emissive: 0xffa800,
    emissiveIntensity: 0.3,
    roughness: 0.2
  });
  const pupilMat = new THREE.MeshBasicMaterial({ color: 0x1a120b });
  const goldBellMat = new THREE.MeshStandardMaterial({
    color: 0xe5b842,
    metalness: 0.85,
    roughness: 0.2
  });

  const limbSpecs: LimbRigSpec[] = [];
  const bonesMap: Record<string, THREE.Bone> = {};
  const bonesList: THREE.Bone[] = [];

  const createPart = (
    name: RagdollPartName,
    center: [number, number, number],
    halfH: number,
    rad: number,
    mass: number,
    parentName?: RagdollPartName,
    pivot?: [number, number, number],
    axis?: [number, number, number],
    limits?: [number, number]
  ): THREE.Group => {
    const visualGroup = new THREE.Group();
    visualGroup.name = `Mesh_${name}`;
    visualGroup.position.set(center[0], center[1], center[2]);
    root.add(visualGroup);

    const bone = new THREE.Bone();
    bone.name = name;
    bonesMap[name] = bone;
    bonesList.push(bone);

    if (parentName && bonesMap[parentName]) {
      const parentSpec = limbSpecs.find((s) => s.name === parentName)!;
      bone.position.set(
        center[0] - parentSpec.centerWorldPos.x,
        center[1] - parentSpec.centerWorldPos.y,
        center[2] - parentSpec.centerWorldPos.z
      );
      bonesMap[parentName].add(bone);
    } else {
      bone.position.set(center[0], center[1], center[2]);
      root.add(bone);
    }

    limbSpecs.push({
      name,
      visualGroup,
      bone,
      centerWorldPos: new THREE.Vector3(center[0], center[1], center[2]),
      halfHeight: halfH,
      radius: rad,
      mass,
      parentName,
      jointWorldPivot: pivot ? new THREE.Vector3(pivot[0], pivot[1], pivot[2]) : undefined,
      jointAxis: axis ? { x: axis[0], y: axis[1], z: axis[2] } : undefined,
      jointLimits: limits
    });

    return visualGroup;
  };

  // 1. PELVIS (Таз — центр (0, 0.44, 0))
  const pelvisG = createPart('Pelvis', [0, 0.44, 0], 0.04, 0.14, 5.0);
  const pelvisMesh = new THREE.Mesh(new THREE.SphereGeometry(0.165, 18, 14), pantsMat);
  pelvisMesh.scale.set(1.12, 0.85, 1.0);
  pelvisMesh.castShadow = true;
  pelvisG.add(pelvisMesh);

  // 2. TORSO (Торс — центр (0, 0.64, 0), сустав поясницы в (0, 0.52, 0))
  const torsoG = createPart(
    'Torso',
    [0, 0.64, 0],
    0.07,
    0.14,
    5.5,
    'Pelvis',
    [0, 0.52, 0],
    [1, 0, 0],
    [-0.25, 0.35]
  );
  const torsoMesh = new THREE.Mesh(new THREE.CylinderGeometry(0.155, 0.17, 0.26, 18), greenSweaterMat);
  torsoMesh.castShadow = true;
  torsoG.add(torsoMesh);

  const sweaterHem = new THREE.Mesh(new THREE.TorusGeometry(0.165, 0.028, 10, 22), greenSweaterMat);
  sweaterHem.rotation.x = Math.PI / 2;
  sweaterHem.position.y = -0.11;
  torsoG.add(sweaterHem);

  const scarfRing = new THREE.Mesh(new THREE.TorusGeometry(0.15, 0.042, 10, 22), scarfMat);
  scarfRing.rotation.x = Math.PI / 2;
  scarfRing.position.y = 0.13;
  scarfRing.castShadow = true;
  torsoG.add(scarfRing);

  const scarfTail = new THREE.Mesh(new THREE.BoxGeometry(0.08, 0.2, 0.032), scarfMat);
  scarfTail.position.set(-0.06, 0.03, 0.165);
  scarfTail.rotation.z = 0.08;
  torsoG.add(scarfTail);

  // 3. HEAD (Голова — центр (0, 0.98, 0), сустав шеи в (0, 0.78, 0))
  const headG = createPart(
    'Head',
    [0, 0.98, 0],
    0.02,
    0.20,
    2.0,
    'Torso',
    [0, 0.78, 0],
    [1, 0, 0],
    [-0.3, 0.3]
  );
  const headSphere = new THREE.Mesh(new THREE.SphereGeometry(0.215, 24, 24), redKnitMat);
  headSphere.scale.set(1.0, 1.08, 1.02);
  headSphere.castShadow = true;
  headG.add(headSphere);

  const beanieCuff = new THREE.Mesh(new THREE.TorusGeometry(0.20, 0.04, 10, 24), redKnitMat);
  beanieCuff.rotation.x = Math.PI / 2 - 0.1;
  beanieCuff.position.set(0, 0.07, 0);
  headG.add(beanieCuff);

  const beanieTip = new THREE.Mesh(new THREE.ConeGeometry(0.115, 0.2, 14), redKnitMat);
  beanieTip.position.set(0, 0.14, -0.14);
  beanieTip.rotation.x = -0.95;
  headG.add(beanieTip);

  const eyeSlot = new THREE.Mesh(new THREE.BoxGeometry(0.23, 0.082, 0.08), skinMat);
  eyeSlot.position.set(0, 0.0, 0.18);
  headG.add(eyeSlot);

  [-0.062, 0.062].forEach((ex, i) => {
    const eyeBall = new THREE.Mesh(new THREE.SphereGeometry(0.04, 12, 12), eyeYellowMat);
    eyeBall.position.set(ex, 0.0, 0.205);
    headG.add(eyeBall);

    const pupil = new THREE.Mesh(new THREE.SphereGeometry(0.02, 8, 8), pupilMat);
    pupil.position.set(ex, 0.0, 0.24);
    headG.add(pupil);

    const brow = new THREE.Mesh(new THREE.BoxGeometry(0.09, 0.024, 0.04), redKnitMat);
    brow.position.set(ex, 0.036, 0.22);
    brow.rotation.z = i === 0 ? -0.24 : 0.24;
    headG.add(brow);
  });

  [-1, 1].forEach((side) => {
    const ear = new THREE.Mesh(new THREE.ConeGeometry(0.062, 0.21, 12), skinMat);
    ear.position.set(side * 0.265, 0.01, -0.01);
    ear.rotation.z = -side * 1.15;
    ear.rotation.y = -side * 0.18;
    ear.castShadow = true;
    headG.add(ear);
  });

  // 4. РУКИ (Плечи и предплечья с варежками — вертикально вдоль тела, суставы в плечах и локтях)
  const buildArm = (side: -1 | 1) => {
    const suffix = side === -1 ? 'L' : 'R';
    const shoulderX = side * 0.22;

    // UpperArm: сустав плеча в (shoulderX, 0.73, 0), центр в (shoulderX, 0.64, 0)
    const uArmG = createPart(
      `UpperArm_${suffix}` as RagdollPartName,
      [shoulderX, 0.64, 0],
      0.055,
      0.052,
      0.9,
      'Torso',
      [shoulderX, 0.73, 0],
      [1, 0, 0],
      [-1.8, 1.3]
    );
    const shoulderBall = new THREE.Mesh(new THREE.SphereGeometry(0.058, 10, 10), greenSweaterMat);
    shoulderBall.position.y = 0.08;
    uArmG.add(shoulderBall);

    const uCyl = new THREE.Mesh(new THREE.CylinderGeometry(0.052, 0.055, 0.17, 12), greenSweaterMat);
    uCyl.castShadow = true;
    uArmG.add(uCyl);

    // LowerArm: сустав локтя в (shoulderX, 0.55, 0), центр в (shoulderX, 0.44, 0)
    const lArmG = createPart(
      `LowerArm_${suffix}` as RagdollPartName,
      [shoulderX, 0.44, 0],
      0.06,
      0.055,
      0.8,
      `UpperArm_${suffix}` as RagdollPartName,
      [shoulderX, 0.55, 0],
      [1, 0, 0],
      [-1.9, 0.1]
    );
    const lCyl = new THREE.Mesh(new THREE.CylinderGeometry(0.052, 0.058, 0.13, 12), greenSweaterMat);
    lCyl.position.y = 0.04;
    lCyl.castShadow = true;
    lArmG.add(lCyl);

    const mitten = new THREE.Mesh(new THREE.SphereGeometry(0.068, 12, 12), mittenMat);
    mitten.scale.set(0.95, 1.18, 0.88);
    mitten.position.y = -0.065;
    mitten.castShadow = true;
    lArmG.add(mitten);

    const thumb = new THREE.Mesh(new THREE.SphereGeometry(0.03, 8, 8), mittenMat);
    thumb.position.set(-side * 0.05, -0.045, 0.03);
    lArmG.add(thumb);
  };
  buildArm(-1);
  buildArm(1);

  // 5. НОГИ (Бедра и голени с ботинками)
  const buildLeg = (side: -1 | 1) => {
    const suffix = side === -1 ? 'L' : 'R';
    const hipX = side * 0.105;

    // UpperLeg: сустав бедра в (hipX, 0.38, 0), центр в (hipX, 0.28, 0)
    const uLegG = createPart(
      `UpperLeg_${suffix}` as RagdollPartName,
      [hipX, 0.28, 0],
      0.055,
      0.068,
      2.2,
      'Pelvis',
      [hipX, 0.38, 0],
      [1, 0, 0],
      [-1.0, 1.0]
    );
    const puff = new THREE.Mesh(new THREE.SphereGeometry(0.092, 12, 12), pantsMat);
    puff.scale.set(0.95, 1.25, 0.95);
    puff.castShadow = true;
    uLegG.add(puff);

    // LowerLeg: сустав колена в (hipX, 0.19, 0), центр в (hipX, 0.095, 0)
    const lLegG = createPart(
      `LowerLeg_${suffix}` as RagdollPartName,
      [hipX, 0.095, 0],
      0.045,
      0.065,
      2.0,
      `UpperLeg_${suffix}` as RagdollPartName,
      [hipX, 0.19, 0],
      [1, 0, 0],
      [0.0, 1.6]
    );
    const bootAnkle = new THREE.Mesh(new THREE.CylinderGeometry(0.056, 0.068, 0.13, 12), bootMat);
    bootAnkle.position.y = 0.02;
    bootAnkle.castShadow = true;
    lLegG.add(bootAnkle);

    const bootFoot = new THREE.Mesh(new THREE.SphereGeometry(0.072, 12, 12), bootMat);
    bootFoot.scale.set(0.95, 0.68, 1.55);
    bootFoot.position.set(0, -0.045, 0.04);
    bootFoot.castShadow = true;
    lLegG.add(bootFoot);

    const curlToe = new THREE.Mesh(new THREE.ConeGeometry(0.042, 0.1, 10), bootMat);
    curlToe.position.set(0, -0.01, 0.145);
    curlToe.rotation.x = -0.45;
    lLegG.add(curlToe);

    [-0.058, 0.058].forEach((bx) => {
      const bell = new THREE.Mesh(new THREE.SphereGeometry(0.018, 8, 8), goldBellMat);
      bell.position.set(bx, 0.04, 0.025);
      lLegG.add(bell);
    });
  };
  buildLeg(-1);
  buildLeg(1);

  return {
    root,
    skeleton: new THREE.Skeleton(bonesList),
    bones: bonesMap,
    limbSpecs,
    animations: createProceduralClips(),
    hasHumanoidRig: true
  };
}

/**
 * 2. УТВЕРЖДЕННЫЙ ДЕД В СТЕГАНОМ ХАЛАТЕ И ОЧКАХ (Grumpy Grandpa)
 */
export function buildApprovedGrandpa(): BuiltCharacterAsset {
  const root = new THREE.Group();
  root.name = 'GrumpyGrandpa_Approved';

  const robeMat = new THREE.MeshStandardMaterial({
    color: 0x1b3b6f,
    map: createKnitTexture(0x1b3b6f),
    roughness: 0.75
  });
  const robeTrimMat = new THREE.MeshStandardMaterial({
    color: 0x2c5282,
    roughness: 0.85
  });
  const capMat = new THREE.MeshStandardMaterial({
    map: createKnitTexture(0x9b2c2c, 0xe2d9c8),
    roughness: 0.85
  });
  const beardMat = new THREE.MeshStandardMaterial({
    color: 0xc4c2bc,
    roughness: 0.9
  });
  const skinMat = new THREE.MeshStandardMaterial({
    color: 0xe6a88a,
    roughness: 0.7
  });
  const glassesGlowMat = new THREE.MeshStandardMaterial({
    color: 0xffffff,
    emissive: 0xfffbeb,
    emissiveIntensity: 0.9,
    roughness: 0.1
  });
  const slipperMat = new THREE.MeshStandardMaterial({
    color: 0x7c5836,
    roughness: 0.95
  });
  const antlerMat = new THREE.MeshStandardMaterial({
    color: 0x3f2716,
    roughness: 0.85
  });

  const limbSpecs: LimbRigSpec[] = [];
  const bonesMap: Record<string, THREE.Bone> = {};
  const bonesList: THREE.Bone[] = [];

  const createPart = (
    name: RagdollPartName,
    center: [number, number, number],
    halfH: number,
    rad: number,
    mass: number,
    parentName?: RagdollPartName,
    pivot?: [number, number, number],
    axis?: [number, number, number],
    limits?: [number, number]
  ): THREE.Group => {
    const visualGroup = new THREE.Group();
    visualGroup.name = `Mesh_${name}`;
    visualGroup.position.set(center[0], center[1], center[2]);
    root.add(visualGroup);

    const bone = new THREE.Bone();
    bone.name = name;
    bonesMap[name] = bone;
    bonesList.push(bone);

    if (parentName && bonesMap[parentName]) {
      const parentSpec = limbSpecs.find((s) => s.name === parentName)!;
      bone.position.set(
        center[0] - parentSpec.centerWorldPos.x,
        center[1] - parentSpec.centerWorldPos.y,
        center[2] - parentSpec.centerWorldPos.z
      );
      bonesMap[parentName].add(bone);
    } else {
      bone.position.set(center[0], center[1], center[2]);
      root.add(bone);
    }

    limbSpecs.push({
      name,
      visualGroup,
      bone,
      centerWorldPos: new THREE.Vector3(center[0], center[1], center[2]),
      halfHeight: halfH,
      radius: rad,
      mass,
      parentName,
      jointWorldPivot: pivot ? new THREE.Vector3(pivot[0], pivot[1], pivot[2]) : undefined,
      jointAxis: axis ? { x: axis[0], y: axis[1], z: axis[2] } : undefined,
      jointLimits: limits
    });

    return visualGroup;
  };

  // Pelvis
  const pelvisG = createPart('Pelvis', [0, 0.56, 0], 0.06, 0.22, 6.0);
  const robeBottom = new THREE.Mesh(new THREE.CylinderGeometry(0.26, 0.29, 0.32, 16), robeMat);
  robeBottom.castShadow = true;
  pelvisG.add(robeBottom);
  const ropeBelt = new THREE.Mesh(new THREE.TorusGeometry(0.265, 0.025, 8, 24), antlerMat);
  ropeBelt.rotation.x = Math.PI / 2;
  ropeBelt.position.y = 0.12;
  pelvisG.add(ropeBelt);

  // Torso
  const torsoG = createPart('Torso', [0, 0.86, 0], 0.08, 0.24, 6.5, 'Pelvis', [0, 0.71, 0], [1, 0, 0], [-0.25, 0.35]);
  const robeTop = new THREE.Mesh(new THREE.CylinderGeometry(0.24, 0.26, 0.34, 16), robeMat);
  robeTop.castShadow = true;
  torsoG.add(robeTop);
  const collar = new THREE.Mesh(new THREE.TorusGeometry(0.18, 0.05, 10, 20), robeTrimMat);
  collar.rotation.x = Math.PI / 2 + 0.25;
  collar.position.set(0, 0.15, 0.03);
  torsoG.add(collar);

  // Head
  const headG = createPart('Head', [0, 1.22, 0], 0.02, 0.21, 2.2, 'Torso', [0, 1.03, 0], [1, 0, 0], [-0.28, 0.28]);
  const headBase = new THREE.Mesh(new THREE.SphereGeometry(0.20, 18, 18), skinMat);
  headBase.castShadow = true;
  headG.add(headBase);
  const beard = new THREE.Mesh(new THREE.SphereGeometry(0.20, 16, 16), beardMat);
  beard.scale.set(1.05, 0.9, 1.05);
  beard.position.set(0, -0.08, 0.06);
  headG.add(beard);
  [-1, 1].forEach((side) => {
    const stache = new THREE.Mesh(new THREE.ConeGeometry(0.07, 0.22, 10), beardMat);
    stache.position.set(side * 0.095, -0.02, 0.21);
    stache.rotation.z = side * 1.25;
    headG.add(stache);
  });
  const bigNose = new THREE.Mesh(new THREE.SphereGeometry(0.062, 12, 12), skinMat);
  bigNose.scale.set(0.9, 1.15, 1.15);
  bigNose.position.set(0, 0.03, 0.22);
  headG.add(bigNose);
  [-0.072, 0.072].forEach((gx) => {
    const lens = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.05, 0.02, 16), glassesGlowMat);
    lens.rotation.x = Math.PI / 2;
    lens.position.set(gx, 0.07, 0.2);
    headG.add(lens);
  });
  const capDome = new THREE.Mesh(new THREE.ConeGeometry(0.21, 0.38, 16), capMat);
  capDome.position.set(0, 0.22, -0.05);
  capDome.rotation.x = -0.55;
  headG.add(capDome);
  const pom = new THREE.Mesh(new THREE.SphereGeometry(0.052, 10, 10), beardMat);
  pom.position.set(0, 0.04, -0.3);
  headG.add(pom);

  // Arms
  const buildArm = (side: -1 | 1) => {
    const suffix = side === -1 ? 'L' : 'R';
    const sx = side * 0.31;
    const uArmG = createPart(`UpperArm_${suffix}` as RagdollPartName, [sx, 0.84, 0], 0.07, 0.07, 1.1, 'Torso', [sx, 0.96, 0], [1, 0, 0], [-1.6, 1.2]);
    uArmG.add(new THREE.Mesh(new THREE.CylinderGeometry(0.072, 0.08, 0.22, 12), robeMat));

    const lArmG = createPart(`LowerArm_${suffix}` as RagdollPartName, [sx, 0.62, 0], 0.07, 0.07, 0.9, `UpperArm_${suffix}` as RagdollPartName, [sx, 0.73, 0], [1, 0, 0], [-1.8, 0.1]);
    const cuff = new THREE.Mesh(new THREE.CylinderGeometry(0.08, 0.09, 0.15, 12), robeTrimMat);
    cuff.position.y = 0.03;
    lArmG.add(cuff);
    const hand = new THREE.Mesh(new THREE.SphereGeometry(0.062, 10, 10), skinMat);
    hand.position.y = -0.08;
    lArmG.add(hand);
  };
  buildArm(-1);
  buildArm(1);

  // Legs
  const buildLeg = (side: -1 | 1) => {
    const suffix = side === -1 ? 'L' : 'R';
    const lx = side * 0.13;
    const uLegG = createPart(`UpperLeg_${suffix}` as RagdollPartName, [lx, 0.34, 0], 0.06, 0.05, 2.2, 'Pelvis', [lx, 0.44, 0], [1, 0, 0], [-0.9, 0.9]);
    uLegG.add(new THREE.Mesh(new THREE.CylinderGeometry(0.032, 0.028, 0.2, 8), skinMat));

    const lLegG = createPart(`LowerLeg_${suffix}` as RagdollPartName, [lx, 0.12, 0], 0.05, 0.065, 2.2, `UpperLeg_${suffix}` as RagdollPartName, [lx, 0.23, 0], [1, 0, 0], [0.0, 1.5]);
    const shin = new THREE.Mesh(new THREE.CylinderGeometry(0.028, 0.026, 0.16, 8), skinMat);
    shin.position.y = 0.03;
    lLegG.add(shin);

    const slipper = new THREE.Mesh(new THREE.SphereGeometry(0.092, 12, 12), slipperMat);
    slipper.scale.set(1.0, 0.65, 1.5);
    slipper.position.set(0, -0.055, 0.04);
    slipper.castShadow = true;
    lLegG.add(slipper);

    const rNose = new THREE.Mesh(new THREE.SphereGeometry(0.02, 8, 8), new THREE.MeshStandardMaterial({ color: 0xdc2626 }));
    rNose.position.set(0, -0.035, 0.175);
    lLegG.add(rNose);
  };
  buildLeg(-1);
  buildLeg(1);

  return {
    root,
    skeleton: new THREE.Skeleton(bonesList),
    bones: bonesMap,
    limbSpecs,
    animations: createProceduralClips(),
    hasHumanoidRig: true
  };
}

/**
 * 3. УТВЕРЖДЕННЫЙ ТОЛСТЫЙ ВОЙЛОЧНЫЙ КОТ (Chubby Felted Cat)
 */
export function buildApprovedCat(): BuiltCharacterAsset {
  const root = new THREE.Group();
  root.name = 'ChubbyCat_Approved';

  const orangeFelt = new THREE.MeshStandardMaterial({
    color: 0xd97724,
    map: createKnitTexture(0xd97724, 0xb85614),
    roughness: 0.92
  });
  const whiteFelt = new THREE.MeshStandardMaterial({
    color: 0xf5efe6,
    roughness: 0.95
  });
  const collarMat = new THREE.MeshStandardMaterial({
    color: 0xb91c1c,
    map: createKnitTexture(0xb91c1c),
    roughness: 0.85
  });
  const bellMat = new THREE.MeshStandardMaterial({
    color: 0xeab308,
    metalness: 0.85,
    roughness: 0.2
  });
  const eyeMat = new THREE.MeshStandardMaterial({
    color: 0xfacc15,
    emissive: 0xeab308,
    emissiveIntensity: 0.4
  });

  const body = new THREE.Mesh(new THREE.SphereGeometry(0.28, 20, 20), orangeFelt);
  body.scale.set(1.15, 0.95, 1.35);
  body.position.set(0, 0.32, 0);
  body.castShadow = true;
  root.add(body);

  const chest = new THREE.Mesh(new THREE.SphereGeometry(0.24, 16, 16), whiteFelt);
  chest.scale.set(1.05, 0.9, 1.0);
  chest.position.set(0, 0.3, 0.14);
  root.add(chest);

  const head = new THREE.Mesh(new THREE.SphereGeometry(0.22, 20, 20), orangeFelt);
  head.scale.set(1.15, 0.95, 1.0);
  head.position.set(0, 0.52, 0.22);
  head.castShadow = true;
  root.add(head);

  const cheeks = new THREE.Mesh(new THREE.SphereGeometry(0.18, 16, 16), whiteFelt);
  cheeks.scale.set(1.25, 0.75, 0.95);
  cheeks.position.set(0, 0.46, 0.27);
  root.add(cheeks);

  const collar = new THREE.Mesh(new THREE.TorusGeometry(0.22, 0.038, 10, 24), collarMat);
  collar.rotation.x = Math.PI / 2 + 0.35;
  collar.position.set(0, 0.41, 0.18);
  root.add(collar);

  const bell = new THREE.Mesh(new THREE.SphereGeometry(0.042, 12, 12), bellMat);
  bell.position.set(0, 0.32, 0.38);
  root.add(bell);

  [-0.085, 0.085].forEach((ex) => {
    const eye = new THREE.Mesh(new THREE.SphereGeometry(0.04, 12, 12), eyeMat);
    eye.position.set(ex, 0.53, 0.4);
    root.add(eye);
  });

  [-0.14, 0.14].forEach((ux) => {
    const ear = new THREE.Mesh(new THREE.ConeGeometry(0.065, 0.12, 8), orangeFelt);
    ear.position.set(ux, 0.7, 0.2);
    ear.rotation.z = ux < 0 ? 0.25 : -0.25;
    root.add(ear);
  });

  [
    [-0.16, 0.1, 0.22],
    [0.16, 0.1, 0.22],
    [-0.16, 0.1, -0.2],
    [0.16, 0.1, -0.2]
  ].forEach((lp) => {
    const paw = new THREE.Mesh(new THREE.CylinderGeometry(0.065, 0.06, 0.2, 10), whiteFelt);
    paw.position.set(lp[0], lp[1], lp[2]);
    paw.castShadow = true;
    root.add(paw);
  });

  const tail = new THREE.Mesh(new THREE.CylinderGeometry(0.055, 0.07, 0.36, 10), orangeFelt);
  tail.position.set(0, 0.28, -0.42);
  tail.rotation.x = -0.8;
  root.add(tail);

  return {
    root,
    skeleton: new THREE.Skeleton([]),
    bones: {},
    limbSpecs: [],
    animations: [],
    hasHumanoidRig: false
  };
}

function createProceduralClips(): THREE.AnimationClip[] {
  const times = [0, 0.25, 0.5, 0.75, 1.0];
  const q1 = new THREE.Quaternion().setFromEuler(new THREE.Euler(0.55, 0, 0));
  const q2 = new THREE.Quaternion().setFromEuler(new THREE.Euler(-0.55, 0, 0));
  const q0 = new THREE.Quaternion();

  const walkClip = new THREE.AnimationClip('Clumsy_Walk', 1.0, [
    new THREE.QuaternionKeyframeTrack('UpperLeg_L.quaternion', times, [...q0.toArray(), ...q1.toArray(), ...q0.toArray(), ...q2.toArray(), ...q0.toArray()]),
    new THREE.QuaternionKeyframeTrack('UpperLeg_R.quaternion', times, [...q0.toArray(), ...q2.toArray(), ...q0.toArray(), ...q1.toArray(), ...q0.toArray()]),
    new THREE.QuaternionKeyframeTrack('UpperArm_L.quaternion', times, [...q0.toArray(), ...q2.toArray(), ...q0.toArray(), ...q1.toArray(), ...q0.toArray()]),
    new THREE.QuaternionKeyframeTrack('UpperArm_R.quaternion', times, [...q0.toArray(), ...q1.toArray(), ...q0.toArray(), ...q2.toArray(), ...q0.toArray()])
  ]);

  const qb1 = new THREE.Quaternion().setFromEuler(new THREE.Euler(0.05, 0, 0.03));
  const idleClip = new THREE.AnimationClip('Idle_Breathing', 2.0, [
    new THREE.QuaternionKeyframeTrack('Torso.quaternion', [0, 1.0, 2.0], [...q0.toArray(), ...qb1.toArray(), ...q0.toArray()])
  ]);

  const qs1 = new THREE.Quaternion().setFromEuler(new THREE.Euler(0.3, 0, 0));
  const qs2 = new THREE.Quaternion().setFromEuler(new THREE.Euler(-0.3, 0, 0));
  const sneakClip = new THREE.AnimationClip('Sneak_TipToe', 1.4, [
    new THREE.QuaternionKeyframeTrack('UpperLeg_L.quaternion', [0, 0.35, 0.7, 1.05, 1.4], [...q0.toArray(), ...qs1.toArray(), ...q0.toArray(), ...qs2.toArray(), ...q0.toArray()]),
    new THREE.QuaternionKeyframeTrack('UpperLeg_R.quaternion', [0, 0.35, 0.7, 1.05, 1.4], [...q0.toArray(), ...qs2.toArray(), ...q0.toArray(), ...qs1.toArray(), ...q0.toArray()])
  ]);

  return [idleClip, walkClip, sneakClip];
}
