-- Server-side elf: movement modes, stamina, jetpack fuel, footstep/landing noise, wall bumps,
-- toy trips, elf-vs-elf bonks, hiding spots, knockdowns and "help a friend up".
-- The client only tells us what it WANTS (sprint / crouch / thrust); speed and noise are decided
-- here, and noise is derived from the velocity the server observes, not from what the client claims.

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared.Config)
local Remotes = require(Shared.Remotes)
local Signal = require(Shared.Signal)

local NoiseService = require(script.Parent.NoiseService)
local ProfileService = require(script.Parent.ProfileService)
local Ragdoll = require(script.Parent.Ragdoll)

local ElfConfig = Config.Elf
local NoiseConfig = Config.Noise
local JetConfig = Config.Jetpack

local ElfService = {}

-- Fires with (player, reason): "Bump", "Land", "Toy", "Bonk" or "Caught".
ElfService.Fell = Signal.new()
-- Fires with (player, helper).
ElfService.HelpedUp = Signal.new()

local house
local states = {}
local spawnResolver
local bonkExempt

local function newState()
	return {
		wantSprint = false,
		wantCrouch = false,
		wantJet = false,
		mode = "Walk",
		appliedMode = nil,
		baseHipHeight = nil,
		speedMultiplier = 1,
		stamina = ElfConfig.MaxStamina,
		regenAt = 0,
		exhausted = false,
		fuel = JetConfig.MaxFuel,
		jetActive = false,
		nextJetNoiseAt = 0,
		stepTimer = 0,
		airborne = false,
		maxFallSpeed = 0,
		bumpReadyAt = 0,
		isDown = false,
		downUntil = 0,
		hidden = false,
		helpPrompt = nil,
		noise = NoiseConfig.Idle,
		published = {},
		-- Per-frame cache for pairwise checks.
		position = nil,
		velocity = nil,
	}
end

local function getParts(player)
	local character = player.Character
	if not character then
		return nil
	end
	local humanoid = character:FindFirstChildOfClass("Humanoid")
	local root = character:FindFirstChild("HumanoidRootPart")
	if humanoid and root and humanoid.Health > 0 then
		return character, humanoid, root
	end
	return nil
end

local function setAttribute(player, state, name, value)
	if state.published[name] ~= value then
		state.published[name] = value
		player:SetAttribute(name, value)
	end
end

local function floorInfo(character, root)
	local params = RaycastParams.new()
	params.FilterType = Enum.RaycastFilterType.Exclude
	params.FilterDescendantsInstances = { character }
	local result = workspace:Raycast(root.Position, Vector3.new(0, -8, 0), params)
	if not result then
		return "Wood", false, false
	end
	local floor = result.Instance
	return floor:GetAttribute("Surface") or "Wood", floor:GetAttribute("Creaky") == true, floor:GetAttribute("Soft") == true
end

local function applyMode(state, humanoid)
	if state.appliedMode == state.mode then
		return
	end
	state.appliedMode = state.mode

	if state.baseHipHeight == nil then
		state.baseHipHeight = humanoid.HipHeight
	end

	local speed = ElfConfig.WalkSpeed
	if state.mode == "Sprint" then
		speed = ElfConfig.SprintSpeed
	elseif state.mode == "Crouch" then
		speed = ElfConfig.CrouchSpeed
	end
	humanoid.WalkSpeed = speed * state.speedMultiplier

	local hipOffset = state.mode == "Crouch" and ElfConfig.CrouchHipOffset or 0
	humanoid.HipHeight = math.max(0, state.baseHipHeight - hipOffset)
end

local function publish(player, state)
	setAttribute(player, state, "Noise", math.floor(state.noise + 0.5))
	setAttribute(player, state, "Stamina", math.floor(state.stamina + 0.5))
	setAttribute(player, state, "Fuel", math.floor(state.fuel + 0.5))
	setAttribute(player, state, "JetActive", state.jetActive)
	setAttribute(player, state, "Hidden", state.hidden)
	setAttribute(player, state, "Down", state.isDown)
end

local function emitNoise(player, state, position, loudness, kind)
	NoiseService.Emit(position, loudness, kind, player)
	state.noise = math.max(state.noise, math.clamp(loudness, 0, 100))
end

function ElfService.IsDown(player)
	local state = states[player]
	return state ~= nil and state.isDown
end

function ElfService.IsCrouching(player)
	local state = states[player]
	return state ~= nil and state.mode == "Crouch" and not state.isDown
end

-- Sneaking under a table or bed: Grandpa only notices from point-blank range.
function ElfService.IsHidden(player)
	local state = states[player]
	return state ~= nil and state.hidden
end

-- Used by GiftService: heavy gifts slow the elf down and disable sprinting.
function ElfService.SetSpeedMultiplier(player, multiplier)
	local state = states[player]
	if state then
		state.speedMultiplier = multiplier
		state.appliedMode = nil
	end
end

function ElfService.SetSpawnResolver(resolver)
	spawnResolver = resolver
end

-- `exempt(playerA, playerB)` returns true for pairs that must not bonk (co-carrying a bicycle).
function ElfService.SetBonkExempt(exempt)
	bonkExempt = exempt
end

-- Moves the elf. Yields briefly while the target area streams in.
function ElfService.Teleport(player, cframe)
	pcall(function()
		player:RequestStreamAroundAsync(cframe.Position, 2)
	end)
	local character = player.Character
	if character and character.Parent then
		character:PivotTo(cframe)
	end
end

-- Knocks the elf over. `velocity` is applied on the owning client (it simulates the character).
function ElfService.Knockdown(player, velocity, reason, downTime)
	local state = states[player]
	local character = getParts(player)
	if not state or not character or state.isDown then
		return
	end

	local now = os.clock()
	local coop = #Players:GetPlayers() > 1
	downTime = downTime or (coop and ElfConfig.DownTimeCoop or ElfConfig.DownTimeSolo)
	state.isDown = true
	state.jetActive = false
	state.downUntil = now + downTime
	state.bumpReadyAt = now + downTime + ElfConfig.BumpCooldown
	if state.helpPrompt then
		state.helpPrompt.Enabled = coop
	end

	Ragdoll.Enable(character)
	Remotes.Knockdown:FireClient(player, velocity)
	ElfService.Fell:Fire(player, reason)
end

function ElfService.Recover(player)
	local state = states[player]
	if not state then
		return
	end
	state.isDown = false
	state.airborne = false
	state.maxFallSpeed = 0
	state.appliedMode = nil
	if state.helpPrompt then
		state.helpPrompt.Enabled = false
	end
	local character = player.Character
	if character then
		Ragdoll.Disable(character)
	end
end

local function updateLanding(player, state, character, root, velocity, grounded)
	if not grounded then
		state.airborne = true
		state.maxFallSpeed = math.max(state.maxFallSpeed, -velocity.Y)
		return false
	end
	if not state.airborne then
		return false
	end

	state.airborne = false
	local fallSpeed = state.maxFallSpeed
	state.maxFallSpeed = 0
	if fallSpeed < ElfConfig.LandNoiseMinSpeed then
		return false
	end

	local _, _, soft = floorInfo(character, root)
	local span = ElfConfig.LandNoiseMaxSpeed - ElfConfig.LandNoiseMinSpeed
	local alpha = math.clamp((fallSpeed - ElfConfig.LandNoiseMinSpeed) / span, 0, 1)
	local loudness = NoiseConfig.LandingSoft + (NoiseConfig.Landing - NoiseConfig.LandingSoft) * alpha
	if soft then
		loudness *= 0.4
	end
	emitNoise(player, state, root.Position, loudness, "Land")

	if fallSpeed >= ElfConfig.HardLandingSpeed and not soft then
		ElfService.Knockdown(player, Vector3.zero, "Land")
		return true
	end
	return false
end

local function updateSteps(player, state, character, root, speed, grounded, dt)
	if not grounded or speed < ElfConfig.MinStepSpeed then
		state.stepTimer = 0
		return
	end

	state.stepTimer -= dt
	if state.stepTimer > 0 then
		return
	end

	-- Classify by observed speed so a tampered client cannot run "quietly".
	local gait = "Walk"
	if speed > ElfConfig.WalkSpeed * 1.15 then
		gait = "Sprint"
	elseif state.mode == "Crouch" and speed <= ElfConfig.CrouchSpeed * 1.25 then
		gait = "Crouch"
	end

	local base = NoiseConfig.Walk
	if gait == "Sprint" then
		base = NoiseConfig.Run
	elseif gait == "Crouch" then
		base = NoiseConfig.Crouch
	end

	local surface, creaky = floorInfo(character, root)
	local boots = ProfileService.GetLevel(player, "Boots")
	local loudness = base
		* (NoiseConfig.SurfaceMultiplier[surface] or 1)
		* math.max(0.25, 1 - boots * ElfConfig.BootsNoiseReductionPerLevel)
	if creaky then
		loudness += NoiseConfig.CreakBonus
	end

	emitNoise(player, state, root.Position, loudness, creaky and "Creak" or "Step")
	state.stepTimer = ElfConfig.StepInterval[gait]
end

local function updateBump(player, state, character, root, horizontal, speed, grounded, now)
	if state.mode ~= "Sprint" or not grounded or now < state.bumpReadyAt then
		return false
	end
	if speed < ElfConfig.SprintSpeed * ElfConfig.BumpMinSpeedFraction then
		return false
	end

	local direction = horizontal.Unit
	local params = RaycastParams.new()
	params.FilterType = Enum.RaycastFilterType.Exclude
	params.FilterDescendantsInstances = { character }
	params.RespectCanCollide = true

	local result = workspace:Raycast(root.Position, direction * ElfConfig.BumpProbeDistance, params)
	if not result or not result.Instance.Anchored then
		return false
	end
	if math.abs(result.Normal.Y) > 0.5 or direction:Dot(-result.Normal) < ElfConfig.BumpMinFacingDot then
		return false
	end

	emitNoise(player, state, root.Position, NoiseConfig.Bump, "Bump")
	local bounce = result.Normal * ElfConfig.BumpBounceSpeed + Vector3.new(0, ElfConfig.BumpPopUpSpeed, 0)
	ElfService.Knockdown(player, bounce, "Bump")
	return true
end

local function updateToys(player, state, root, horizontal, speed, grounded, now)
	if not grounded or state.mode == "Crouch" or speed < ElfConfig.ToyTripMinSpeed or now < state.bumpReadyAt then
		return false
	end
	for _, toy in ipairs(house.Toys) do
		local offset = toy.Position - root.Position
		if math.abs(offset.Y) < 5 and Vector3.new(offset.X, 0, offset.Z).Magnitude < ElfConfig.ToyTripRadius then
			emitNoise(player, state, root.Position, NoiseConfig.Toy, "Toy")
			ElfService.Knockdown(player, horizontal * 0.6 + Vector3.new(0, 12, 0), "Toy")
			return true
		end
	end
	return false
end

local function updateHidden(state, root)
	if state.mode ~= "Crouch" then
		state.hidden = false
		return
	end
	for _, zone in ipairs(house.HidingZones) do
		local point = zone.CFrame:PointToObjectSpace(root.Position)
		local half = zone.Size / 2
		if math.abs(point.X) <= half.X and math.abs(point.Z) <= half.Z and math.abs(point.Y) <= half.Y + 3 then
			state.hidden = true
			return
		end
	end
	state.hidden = false
end

local function updateJetpack(player, state, root, grounded, dt, now)
	local level = ProfileService.GetLevel(player, "Jetpack")
	if level > 0 and state.wantJet and state.fuel > 0 then
		local burn = math.max(JetConfig.MinBurnPerSecond, JetConfig.BaseBurnPerSecond - JetConfig.BurnReductionPerLevel * level)
		state.fuel = math.max(0, state.fuel - burn * dt)
		state.jetActive = true
		if now >= state.nextJetNoiseAt then
			state.nextJetNoiseAt = now + JetConfig.NoiseInterval
			emitNoise(player, state, root.Position, NoiseConfig.Jetpack, "Jetpack")
		end
		return
	end

	state.jetActive = false
	if grounded then
		local refuel = JetConfig.BaseRefuelPerSecond + JetConfig.RefuelPerLevel * level
		state.fuel = math.min(JetConfig.MaxFuel, state.fuel + refuel * dt)
	end
end

local function updatePlayer(player, state, dt, now)
	state.position = nil
	local character, humanoid, root = getParts(player)
	if not character then
		return
	end

	state.noise = math.max(NoiseConfig.Idle, state.noise - NoiseConfig.MeterDecay * dt)

	if state.isDown then
		state.hidden = false
		if now >= state.downUntil then
			ElfService.Recover(player)
		end
		publish(player, state)
		return
	end

	local velocity = root.AssemblyLinearVelocity
	local horizontal = Vector3.new(velocity.X, 0, velocity.Z)
	local speed = horizontal.Magnitude
	local grounded = humanoid.FloorMaterial ~= Enum.Material.Air

	state.position = root.Position
	state.velocity = velocity

	-- Mode and stamina
	local mode = "Walk"
	if state.wantCrouch then
		mode = "Crouch"
	elseif state.wantSprint and not state.exhausted and state.speedMultiplier >= 1 then
		mode = "Sprint"
	end

	if mode == "Sprint" and speed > ElfConfig.MinStepSpeed then
		state.stamina = math.max(0, state.stamina - ElfConfig.SprintDrainPerSecond * dt)
		state.regenAt = now + ElfConfig.StaminaRegenDelay
		if state.stamina <= 0 then
			state.exhausted = true
			mode = "Walk"
		end
	elseif now >= state.regenAt then
		state.stamina = math.min(ElfConfig.MaxStamina, state.stamina + ElfConfig.StaminaRegenPerSecond * dt)
		if state.exhausted and state.stamina >= ElfConfig.ExhaustedRecoverThreshold then
			state.exhausted = false
		end
	end

	state.mode = mode
	applyMode(state, humanoid)
	updateHidden(state, root)
	updateJetpack(player, state, root, grounded, dt, now)

	local fell = updateLanding(player, state, character, root, velocity, grounded)
		or updateBump(player, state, character, root, horizontal, speed, grounded, now)
		or updateToys(player, state, root, horizontal, speed, grounded, now)
	if not fell then
		updateSteps(player, state, character, root, speed, grounded, dt)
	end

	publish(player, state)
end

-- Two elves running into each other head-on both go flying.
local function updateBonks(now)
	local list = {}
	for player, state in pairs(states) do
		if state.position and not state.isDown and now >= state.bumpReadyAt then
			table.insert(list, { Player = player, State = state })
		end
	end

	for i = 1, #list - 1 do
		for j = i + 1, #list do
			local a, b = list[i], list[j]
			if not a.State.isDown and not b.State.isDown then
				local offset = b.State.position - a.State.position
				local distance = offset.Magnitude
				if distance > 0.1 and distance < ElfConfig.BonkDistance then
					local direction = offset / distance
					local closing = (a.State.velocity - b.State.velocity):Dot(direction)
					local exempt = bonkExempt and bonkExempt(a.Player, b.Player)
					if closing >= ElfConfig.BonkClosingSpeed and not exempt then
						NoiseService.Emit(a.State.position, NoiseConfig.Bonk, "Bonk", a.Player)
						local up = Vector3.new(0, 14, 0)
						ElfService.Knockdown(a.Player, -direction * 12 + up, "Bonk")
						ElfService.Knockdown(b.Player, direction * 12 + up, "Bonk")
					end
				end
			end
		end
	end
end

local function createHelpPrompt(player, state, root)
	local prompt = Instance.new("ProximityPrompt")
	prompt.Name = "HelpUpPrompt"
	prompt.ObjectText = player.DisplayName
	prompt.ActionText = "Поднять друга"
	prompt.KeyboardKeyCode = Enum.KeyCode.E
	prompt.MaxActivationDistance = 8
	prompt.RequiresLineOfSight = false
	prompt.Enabled = false
	prompt.Parent = root
	state.helpPrompt = prompt

	prompt.Triggered:Connect(function(helper)
		if helper == player or not state.isDown or ElfService.IsDown(helper) then
			return
		end
		ElfService.Recover(player)
		ElfService.HelpedUp:Fire(player, helper)
	end)
end

local function onCharacterAdded(player, character)
	local humanoid = character:WaitForChild("Humanoid", 10)
	local root = character:WaitForChild("HumanoidRootPart", 10)
	if not humanoid or not root then
		return
	end

	local previous = states[player]
	local state = newState()
	if previous then
		state.wantSprint = previous.wantSprint
		state.wantCrouch = previous.wantCrouch
		state.speedMultiplier = previous.speedMultiplier
	end
	states[player] = state

	humanoid.RequiresNeck = false
	humanoid.BreakJointsOnDeath = false
	humanoid.UseJumpPower = false
	humanoid.JumpHeight = ElfConfig.JumpHeight
	humanoid.WalkSpeed = ElfConfig.WalkSpeed
	createHelpPrompt(player, state, root)

	if not character:IsDescendantOf(workspace) then
		character.AncestryChanged:Wait()
	end
	task.wait(0.1)

	-- Ask where this player belongs right now (lobby, hub, rooftops or house).
	local cframe
	for _ = 1, 50 do
		cframe = spawnResolver and spawnResolver(player)
		if cframe then
			break
		end
		task.wait(0.1)
	end
	if cframe and player.Character == character then
		ElfService.Teleport(player, cframe)
	end
end

local function onPlayerAdded(player)
	states[player] = newState()
	player.CharacterAdded:Connect(function(character)
		onCharacterAdded(player, character)
	end)
	if player.Character then
		task.spawn(onCharacterAdded, player, player.Character)
	end
end

function ElfService.Start(world)
	house = world.House

	Remotes.MoveMode.OnServerEvent:Connect(function(player, wantSprint, wantCrouch)
		local state = states[player]
		if not state or typeof(wantSprint) ~= "boolean" or typeof(wantCrouch) ~= "boolean" then
			return
		end
		state.wantSprint = wantSprint
		state.wantCrouch = wantCrouch
	end)

	Remotes.Jetpack.OnServerEvent:Connect(function(player, wantJet)
		local state = states[player]
		if state and typeof(wantJet) == "boolean" then
			state.wantJet = wantJet
		end
	end)

	Players.PlayerAdded:Connect(onPlayerAdded)
	Players.PlayerRemoving:Connect(function(player)
		states[player] = nil
	end)
	for _, player in ipairs(Players:GetPlayers()) do
		task.spawn(onPlayerAdded, player)
	end

	RunService.Heartbeat:Connect(function(dt)
		local now = os.clock()
		for player, state in pairs(states) do
			updatePlayer(player, state, dt, now)
		end
		updateBonks(now)
	end)
end

return ElfService
