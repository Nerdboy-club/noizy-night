-- All tuning numbers live here.
-- Distances from the Unity/three.js reference are in metres; Roblox uses studs.
-- Convert with StudsPerMeter. Speeds below are already in studs/second unless named *Meters.

local Config = {}

Config.StudsPerMeter = 4

Config.Noise = {
	Idle = 10,
	Walk = 30,
	Run = 55,
	Crouch = 12,
	Landing = 60,
	LandingSoft = 25,
	CreakBonus = 25,
	Bump = 45,
	Bonk = 40,
	Toy = 45,
	Jetpack = 70,
	GiftImpact = 35,
	GlassBreak = 82,
	NoisyTick = 22,
	MusicBox = 62,
	RcCar = 42,
	DoorCreak = 30,
	DoorSlam = 65,
	CatYowl = 65,
	CatMeow = 35,
	-- Hearing range in metres is loudness / FalloffDivisor.
	FalloffDivisor = 6,
	-- Grandpa wakes immediately when a single heard noise reaches this.
	WakeThreshold = 45,
	SurfaceMultiplier = {
		Carpet = 0.5,
		Wood = 1,
		Tile = 1.2,
		Ice = 0.8,
	},
	-- How fast the HUD noise meter falls back to Idle (units per second).
	MeterDecay = 40,
}

Config.Elf = {
	WalkSpeed = 10,
	SprintSpeed = 16,
	CrouchSpeed = 5,
	JumpHeight = 4,
	-- How far the hips sink while crouching (blockout stand-in for an animation).
	CrouchHipOffset = 1,

	MaxStamina = 100,
	SprintDrainPerSecond = 22,
	StaminaRegenPerSecond = 28,
	StaminaRegenDelay = 0.6,
	ExhaustedRecoverThreshold = 25,

	StepInterval = { Walk = 0.46, Sprint = 0.33, Crouch = 0.58 },
	MinStepSpeed = 2,
	BootsNoiseReductionPerLevel = 0.15,

	-- Sprinting head-on into a wall knocks the elf down.
	BumpMinSpeedFraction = 0.85,
	BumpProbeDistance = 2.2,
	BumpMinFacingDot = 0.75,
	BumpCooldown = 2.5,
	BumpBounceSpeed = 14,
	BumpPopUpSpeed = 16,

	-- Two elves running into each other.
	BonkDistance = 3,
	BonkClosingSpeed = 12,

	-- Toys on the floor trip anyone who is not sneaking.
	ToyTripRadius = 1.6,
	ToyTripMinSpeed = 6,

	DownTimeSolo = 1.4,
	DownTimeCoop = 4,
	CaughtDownTimeCoop = 12,

	-- Fall speed (studs/s) mapped onto landing noise; above HardLandingSpeed the elf falls over.
	LandNoiseMinSpeed = 30,
	LandNoiseMaxSpeed = 80,
	HardLandingSpeed = 60,
}

Config.Jetpack = {
	MaxFuel = 100,
	BaseBurnPerSecond = 22,
	BurnReductionPerLevel = 3,
	MinBurnPerSecond = 8,
	BaseRefuelPerSecond = 6,
	RefuelPerLevel = 2,
	BaseThrustMeters = 3.4,
	ThrustPerLevelMeters = 0.4,
	NoiseInterval = 0.3,
}

Config.Grandpa = {
	HipHeight = 4,
	SearchSpeed = 6,
	ChaseSpeed = 18,

	HearingFloor = 4,
	AwarenessGain = 0.6,
	AwakeHearingMultiplier = 1.5,
	ReinvestigateLoudness = 14,
	SpookyHearingMultiplier = 1.35,
	SpookyDecayMultiplier = 0.7,

	StirThreshold = 15,
	SuspiciousThreshold = 40,
	SearchThreshold = 70,
	Hysteresis = 6,
	DecayAsleep = 4,
	DecayStirring = 2.5,
	DecaySuspicious = 2,

	-- Vision (metres / degrees).
	VisionRangeMeters = 7.5,
	VisionFovDegrees = 80,
	NearSenseMeters = 1.1,
	CrouchRangeMultiplier = 0.6,
	HiddenRevealMeters = 0.8,
	DetectRateSuspicious = 0.6,
	DetectRateSearching = 1.4,
	DetectDecay = 0.5,
	LoseSightTime = 3.5,

	CatchDistanceMeters = 1.0,
	HiddenCatchDistanceMeters = 0.6,
	ArriveDistanceMeters = 0.6,

	LookAroundDuration = 3,
	LookAroundAngle = 70,
	LookAroundSweeps = 2,
	SearchTimeout = 20,
	ReturnTimeout = 25,

	TickInterval = 0.1,
}

Config.Cat = {
	WalkSpeed = 6,
	FleeSpeed = 16,
	HipHeight = 1,
	SleepMin = 20,
	SleepMax = 45,
	IdleMin = 4,
	IdleMax = 8,
	HearThreshold = 30,
	TailRadius = 2.2,
	TailCooldown = 5,
	FleeDistance = 12,
	MeowRadius = 8,
	MeowCooldown = 8,
	TickInterval = 0.2,
}

Config.Catnip = {
	Price = 40,
	Max = 3,
	RadiusMeters = 6,
	Duration = 20,
}

Config.Door = {
	CreakAngularSpeed = 2.4,
	SlamAngularSpeed = 4.6,
	CreakCooldown = 0.7,
	SlamCooldown = 1.5,
}

-- Gift kinds. Size is in studs; Sackable gifts fit into the sack [R].
Config.GiftKinds = {
	Small = {
		Name = "Коробка",
		Size = Vector3.new(2, 2, 2),
		Color = Color3.fromRGB(200, 40, 50),
		Sackable = true,
		Hint = "Влезает в мешок [R]",
	},
	Noisy = {
		Name = "Заводная игрушка",
		Size = Vector3.new(2, 2.4, 2),
		Color = Color3.fromRGB(240, 200, 60),
		Sackable = true,
		Hint = "Тикает, пока не в мешке [R]",
	},
	Huge = {
		Name = "Велосипед",
		Size = Vector3.new(2.6, 4, 6.5),
		Color = Color3.fromRGB(60, 150, 90),
		Sackable = false,
		Hint = "Только в руках. Вдвоём быстрее!",
	},
	Fragile = {
		Name = "Снежный шар",
		Size = Vector3.new(2.2, 2.2, 2.2),
		Color = Color3.fromRGB(170, 215, 255),
		Sackable = false,
		Ball = true,
		Hint = "Хрупкое! Не бегите и не падайте",
	},
	MusicBox = {
		Name = "Шкатулка-будильник",
		Size = Vector3.new(2, 1.6, 2),
		Color = Color3.fromRGB(150, 90, 190),
		Sackable = true,
		Hint = "Звенит, если не подкрутить ключ (взять в руки)",
	},
	RcCar = {
		Name = "RC-машинка",
		Size = Vector3.new(1.6, 1.2, 3),
		Color = Color3.fromRGB(240, 130, 40),
		Sackable = true,
		Hint = "На полу уезжает шуметь и отвлекает Деда",
	},
}

Config.Gift = {
	ImpactMinSpeed = 14,
	ImpactDamagePerSpeed = 1.5,
	ImpactMinDamage = 3,
	ImpactMaxDamage = 30,
	ImpactCooldown = 0.35,
	FallDropDamage = 10,
	FragileBreakSpeed = 13.6, -- 3.4 m/s
	NoisyTickInterval = 1,
	MusicBoxWindTime = 35,
	MusicBoxRings = 4,
	MusicBoxRingInterval = 0.6,
	RcCarCharges = 2,
	RcCarDelay = 1,
	RcCarDriveTime = 7,
	RcCarSpeed = 6,
	RcCarBuzzInterval = 0.8,
	HugeSoloSpeed = 0.42,
	HugeGripBonusPerLevel = 0.06,
	HugeDuoSpeed = 0.85,
	CoCarryRopeLength = 4,
}

Config.Economy = {
	StartTips = 120,
	RewardMin = 120,
	RewardMax = 220,
	SpookyChance = 0.2,
	SpookyTipMultiplier = 2,
}

Config.MaxUpgradeLevel = 5
Config.UpgradeOrder = { "Jetpack", "Boots", "Sack", "Grip" }
-- Price of the next level = Base + currentLevel * Step.
Config.Upgrades = {
	Jetpack = { Name = "Реактивный ранец [Q]", Base = 100, Step = 60 },
	Boots = { Name = "Бесшумные ботинки", Base = 75, Step = 50 },
	Sack = { Name = "Мешок (вместимость)", Base = 85, Step = 55 },
	Grip = { Name = "Хват (тяжёлые подарки)", Base = 80, Step = 50 },
}

-- Cosmetic skins (hat + scarf, optional glow). Price is in tips; GamePass skins need that pass instead.
Config.Skins = {
	{ Id = "Classic", Name = "Классика", Price = 0, Hat = Color3.fromRGB(200, 40, 50), Scarf = Color3.fromRGB(245, 240, 230) },
	{ Id = "Snow", Name = "Снежный", Price = 150, Hat = Color3.fromRGB(235, 245, 255), Scarf = Color3.fromRGB(110, 170, 240) },
	{ Id = "Candy", Name = "Карамельный", Price = 300, Hat = Color3.fromRGB(255, 120, 170), Scarf = Color3.fromRGB(255, 255, 255) },
	{ Id = "Forest", Name = "Лесной", Price = 300, Hat = Color3.fromRGB(50, 130, 70), Scarf = Color3.fromRGB(230, 190, 80) },
	{ Id = "Ninja", Name = "Ночной ниндзя", Price = 600, Hat = Color3.fromRGB(35, 35, 50), Scarf = Color3.fromRGB(150, 60, 200) },
	{
		Id = "Gold",
		Name = "Золотой эльф",
		GamePass = "GoldenElf",
		Hat = Color3.fromRGB(255, 205, 70),
		Scarf = Color3.fromRGB(255, 240, 180),
		Glow = Color3.fromRGB(255, 220, 120),
	},
}

-- Robux purchases. Id = 0 means "not created yet": the button is hidden and nothing is charged.
-- Create them in Creator Dashboard (Monetization) and paste the numeric ids here.
Config.GamePasses = {
	Vip = { Id = 0, Name = "VIP-эльф", Description = "Чаевые за каждый рейс ×1.5 и значок VIP над головой" },
	GoldenElf = { Id = 0, Name = "Золотой эльф", Description = "Светящийся золотой скин" },
}
Config.VipTipMultiplier = 1.5

Config.Products = {
	Tips500 = { Id = 0, Name = "Мешочек чаевых", Tips = 500 },
	Tips2000 = { Id = 0, Name = "Сундук чаевых", Tips = 2000 },
}
Config.ProductOrder = { "Tips500", "Tips2000" }
Config.GamePassOrder = { "Vip", "GoldenElf" }

Config.Lobby = {
	-- Reserved servers do not exist in Studio; there the mission always runs in the same server.
	UseReservedServers = true,
	Countdown = 10,
	FullCountdown = 3,
	Pads = {
		{ Name = "Соло", Size = 1 },
		{ Name = "Дуэт", Size = 2 },
		{ Name = "Трио", Size = 3 },
		{ Name = "Отряд", Size = 4 },
	},
	Objective = "Встаньте на площадку: соло, дуэт, трио или отряд.",
}

Config.Round = {
	ResultsTime = 9,
	ChimneyCount = 15,
}

return Config
