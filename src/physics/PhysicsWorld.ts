import RAPIER from '@dimforge/rapier3d-compat';

export class PhysicsWorld {
  public world!: RAPIER.World;
  private isInitialized = false;

  public async init() {
    if (this.isInitialized) return;
    await RAPIER.init();
    // Гравитация стандартная: вниз по Y
    const gravity = { x: 0.0, y: -18.0, z: 0.0 };
    this.world = new RAPIER.World(gravity);
    this.isInitialized = true;
  }

  public step(delta: number) {
    if (!this.isInitialized) return;
    // Ограничиваем delta чтобы физика не взрывалась при переключении вкладок
    this.world.timestep = Math.min(delta, 0.05);
    this.world.step();
  }

  public getRapier() {
    return RAPIER;
  }
}
