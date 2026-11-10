-- Grumpy Grandpa: hears through NoiseService, sees through a raycast vision cone,
-- walks with PathfindingService.
--
-- States: Asleep -> Stirring -> Suspicious (sits up) -> Searching (walks to the noise, looks around)
--         -> Chasing (saw an elf) -> Returning (back to bed) -> Asleep

local PathfindingService = game:GetService("PathfindingService")
local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared.Config)
local Signal = require(Shared.Signal)

local NoiseService = require(script.Parent.NoiseService)
local ElfService = require(script.Parent.ElfService)

local GrandpaConfig = Config.Grandpa
local STUDS = Config.StudsPerMeter

local VISION_RANGE = GrandpaConfig.VisionRangeMeters * STUDS
local NEAR_SENSE = GrandpaConfig.NearSenseMeters * STUDS
local CATCH_DISTANCE = GrandpaConfig.CatchDistanceMeters * STUDS
local HIDDEN_CATCH_DISTANCE = GrandpaConfig.HiddenCatchDistanceMeters * STUDS
local HIDDEN_REVEAL = GrandpaConfig.HiddenRevealMeters * STUDS
local ARRIVE_DISTANCE = GrandpaConfig.ArriveDistanceMeters * STUDS
local FOV_COS = math.cos(math.rad(GrandpaConfig.VisionFovDegrees / 2))
local ROOT_HALF_HEIGHT = 1

local INDICATOR = {
	Asleep = { Text = "Zzz", Color = Color3.fromRGB(170, 200, 255) },
	Stirring = { Text = "…", Color = Color3.fromRGB(230, 230, 200) },
	Suspicious = { Text = "?", Color = Color3.fromRGB(255, 220, 110) },
	Searching = { Text = "??", Color = Color3.fromRGB(255, 160, 70) },
	Chasing = { Text = "!", Color = Color3.fromRGB(255, 70, 60) },
	Returning = { Text = "…", Color = Color3.fromRGB(230, 230, 200) },
}

local HEARD_WORDS = {
	Step = "топ-топ?",
	Creak = "скрип?",
	Land = "бум?",
	Bump = "бах?",
	Gift = "бряк?",
	Toy = "пи-и?",
	Bonk = "бдыщ?",
	Jetpack = "пш-ш-ш?",
	Glass = "дзынь?!",
	Tick = "тик-так?",
	Alarm = "дзынь-дзынь?!",
	Buzz = "вж-ж-ж?",
	DoorCreak = "скрип двери?",
	DoorSlam = "БАХ?!",
	Cat = "кот?!",
	Meow = "мяу?",
}

local GrandpaService = {}

-- Fires with (player) when Grandpa grabs an elf.
GrandpaService.Caught = Signal.new()
-- Fires with (newState, oldState).
GrandpaService.StateChanged = Signal.new()

local house
local model, root, head, humanoid, flashlight
local indicatorLabel, heardLabel

local state = "Asleep"
local active = false
local spooky = false
local awareness = 0
local detect = 0
local noiseTarget
local heardClearAt = 0

local destination
local waypoints
local waypointIndex = 1
local pathRequest = 0
local nextRepathAt = 0
local deadline = 0

local lookStartedAt
local lookBaseCFrame

local chaseTarget
local lastSeenAt = 0
local lastSeenPosition

local function flat(vector)
	return Vector3.new(vector.X, 0, vector.Z)
end

local function flatDistance(a, b)
	return flat(a - b).Magnitude
end

local function onFloor(position)
	return Vector3.new(position.X, house.FloorY, position.Z)
end

local function say(text, seconds)
	heardLabel.Text = text
	heardClearAt = os.clock() + (seconds or 2)
end

local function weldTo(part, offset)
	part.CFrame = root.CFrame * offset
	part.Anchored = false
	part.CanCollide = false
	part.Massless = true
	local weld = Instance.new("WeldConstraint")
	weld.Part0 = root
	weld.Part1 = part
	weld.Parent = part
	part.Parent = model
end

local function makePart(name, size, color, shape)
	local part = Instance.new("Part")
	part.Name = name
	part.Size = size
	part.Color = color
	part.Material = Enum.Material.Fabric
	part.TopSurface = Enum.SurfaceType.Smooth
	part.BottomSurface = Enum.SurfaceType.Smooth
	if shape then
		part.Shape = shape
	end
	return part
end

local function buildRig()
	model = Instance.new("Model")
	model.Name = "Grandpa"

	root = makePart("HumanoidRootPart", Vector3.new(2, ROOT_HALF_HEIGHT * 2, 1), Color3.new(1, 1, 1))
	root.Transparency = 1
	root.Anchored = true
	root.CFrame = house.BedLieCFrame
	root.Parent = model
	model.PrimaryPart = root

	-- Blue quilted robe, big head, striped night cap.
	weldTo(makePart("Robe", Vector3.new(2.6, 6, 1.6), Color3.fromRGB(50, 70, 150)), CFrame.new(0, -1.5, 0))
	head = makePart("Head", Vector3.new(2, 2, 2), Color3.fromRGB(236, 190, 160), Enum.PartType.Ball)
	weldTo(head, CFrame.new(0, 2.5, 0))
	weldTo(makePart("NightCap", Vector3.new(1.6, 1, 1.6), Color3.fromRGB(210, 50, 50)), CFrame.new(0, 3.7, 0))
	weldTo(makePart("Nose", Vector3.new(0.6, 0.6, 0.6), Color3.fromRGB(226, 150, 130), Enum.PartType.Ball), CFrame.new(0, 2.4, -1))

	flashlight = Instance.new("SpotLight")
	flashlight.Face = Enum.NormalId.Front
	flashlight.Angle = 45
	flashlight.Range = VISION_RANGE
	flashlight.Brightness = 3
	flashlight.Color = Color3.fromRGB(255, 240, 200)
	flashlight.Enabled = false
	flashlight.Parent = head

	humanoid = Instance.new("Humanoid")
	humanoid.RigType = Enum.HumanoidRigType.R15
	humanoid.HipHeight = GrandpaConfig.HipHeight
	humanoid.RequiresNeck = false
	humanoid.BreakJointsOnDeath = false
	humanoid.DisplayDistanceType = Enum.HumanoidDisplayDistanceType.None
	humanoid.WalkSpeed = GrandpaConfig.SearchSpeed
	humanoid.Parent = model

	local billboard = Instance.new("BillboardGui")
	billboard.Name = "AlertIndicator"
	billboard.Adornee = head
	billboard.Size = UDim2.fromOffset(220, 90)
	billboard.StudsOffset = Vector3.new(0, 3.5, 0)
	billboard.AlwaysOnTop = true
	billboard.Parent = head

	indicatorLabel = Instance.new("TextLabel")
	indicatorLabel.BackgroundTransparency = 1
	indicatorLabel.Size = UDim2.new(1, 0, 0.6, 0)
	indicatorLabel.Font = Enum.Font.FredokaOne
	indicatorLabel.TextScaled = true
	indicatorLabel.TextStrokeTransparency = 0.3
	indicatorLabel.Parent = billboard

	heardLabel = Instance.new("TextLabel")
	heardLabel.BackgroundTransparency = 1
	heardLabel.Position = UDim2.new(0, 0, 0.6, 0)
	heardLabel.Size = UDim2.new(1, 0, 0.4, 0)
	heardLabel.Font = Enum.Font.GothamMedium
	heardLabel.TextScaled = true
	heardLabel.TextColor3 = Color3.new(1, 1, 1)
	heardLabel.TextStrokeTransparency = 0.3
	heardLabel.Text = ""
	heardLabel.Parent = billboard

	model.Parent = workspace
end

local function isInBed()
	return root.Anchored
end

local function getOutOfBed()
	if not isInBed() then
		return
	end
	local standY = house.FloorY + GrandpaConfig.HipHeight + ROOT_HALF_HEIGHT
	local stand = Vector3.new(house.BedStandPosition.X, standY, house.BedStandPosition.Z)
	model:PivotTo(CFrame.lookAt(stand, stand + Vector3.new(-1, 0, 0)))
	root.Anchored = false
	root.AssemblyLinearVelocity = Vector3.zero
	pcall(function()
		root:SetNetworkOwner(nil)
	end)
end

local function getIntoBed(poseCFrame)
	root.Anchored = true
	model:PivotTo(poseCFrame)
end

local function pathTo(target)
	destination = onFloor(target)
	waypoints = nil
	waypointIndex = 1
	pathRequest += 1
	local request = pathRequest
	local goal = destination

	local path = PathfindingService:CreatePath({
		AgentRadius = 2,
		AgentHeight = 7,
		AgentCanJump = false,
	})
	local ok = pcall(function()
		path:ComputeAsync(onFloor(root.Position), goal)
	end)
	-- ComputeAsync yields; drop the result if a newer request started meanwhile.
	if request ~= pathRequest then
		return
	end
	if ok and path.Status == Enum.PathStatus.Success then
		waypoints = path:GetWaypoints()
		waypointIndex = 2
	end
	-- No path: followPath falls back to walking straight at the destination.
end

-- Returns true once Grandpa has arrived at `destination`.
local function followPath()
	if not destination then
		return true
	end
	if flatDistance(destination, root.Position) <= ARRIVE_DISTANCE then
		return true
	end

	local goal = destination
	if waypoints then
		local waypoint = waypoints[waypointIndex]
		while waypoint and flatDistance(waypoint.Position, root.Position) < 2.5 do
			waypointIndex += 1
			waypoint = waypoints[waypointIndex]
		end
		if waypoint then
			goal = waypoint.Position
		end
	end
	humanoid:MoveTo(goal)
	return false
end

local function stopLooking()
	lookStartedAt = nil
	lookBaseCFrame = nil
	humanoid.AutoRotate = true
end

local function setState(newState)
	local oldState = state
	if oldState == newState then
		return
	end
	state = newState
	stopLooking()

	local indicator = INDICATOR[newState]
	indicatorLabel.Text = indicator.Text
	indicatorLabel.TextColor3 = indicator.Color

	if newState == "Asleep" or newState == "Stirring" then
		getIntoBed(house.BedLieCFrame)
		flashlight.Enabled = false
	elseif newState == "Suspicious" then
		getIntoBed(house.BedSitCFrame)
		flashlight.Enabled = false
	else
		getOutOfBed()
		flashlight.Enabled = true
		humanoid.WalkSpeed = newState == "Chasing" and GrandpaConfig.ChaseSpeed or GrandpaConfig.SearchSpeed
	end

	GrandpaService.StateChanged:Fire(newState, oldState)
end

local function startSearch(target)
	chaseTarget = nil
	detect = 0
	setState("Searching")
	stopLooking()
	deadline = os.clock() + GrandpaConfig.SearchTimeout
	pathTo(target or root.Position)
end

local function startReturn()
	chaseTarget = nil
	detect = 0
	setState("Returning")
	deadline = os.clock() + GrandpaConfig.ReturnTimeout
	pathTo(house.BedStandPosition)
end

local function canSee(player)
	local character = player.Character
	local targetRoot = character and character:FindFirstChild("HumanoidRootPart")
	if not targetRoot then
		return false
	end

	local eye = head.Position
	local toTarget = targetRoot.Position - eye
	local distance = toTarget.Magnitude
	local range = VISION_RANGE
	if ElfService.IsCrouching(player) then
		range *= GrandpaConfig.CrouchRangeMultiplier
	end
	if distance > range then
		return false
	end
	-- An elf sneaking under furniture is only noticed from point-blank range.
	if ElfService.IsHidden(player) and distance > HIDDEN_REVEAL then
		return false
	end

	if distance > NEAR_SENSE then
		local look = flat(head.CFrame.LookVector)
		local direction = flat(toTarget)
		if look.Magnitude < 0.01 or direction.Magnitude < 0.01 then
			return false
		end
		if look.Unit:Dot(direction.Unit) < FOV_COS then
			return false
		end
	end

	local params = RaycastParams.new()
	params.FilterType = Enum.RaycastFilterType.Exclude
	params.FilterDescendantsInstances = { model }
	params.RespectCanCollide = true
	local result = workspace:Raycast(eye, toTarget, params)
	return result == nil or result.Instance:IsDescendantOf(character)
end

local function nearestVisibleElf()
	local best, bestDistance
	for _, player in ipairs(Players:GetPlayers()) do
		if canSee(player) then
			local distance = (player.Character.HumanoidRootPart.Position - root.Position).Magnitude
			if not bestDistance or distance < bestDistance then
				best, bestDistance = player, distance
			end
		end
	end
	return best
end

-- Returns true if Grandpa switched to Chasing.
local function updateVision(dt, rate, now)
	local seen = nearestVisibleElf()
	if not seen then
		detect = math.max(0, detect - GrandpaConfig.DetectDecay * dt)
		return false
	end

	detect += rate * dt
	if detect < 1 then
		return false
	end

	chaseTarget = seen
	lastSeenAt = now
	lastSeenPosition = seen.Character.HumanoidRootPart.Position
	nextRepathAt = 0
	setState("Chasing")
	say("Эльфы?!", 2)
	return true
end

local function catch(player)
	say("Опять эльфы?! Где мой тапок!", 3)
	awareness = 0
	startReturn()
	GrandpaService.Caught:Fire(player)
end

local function tickLookAround(now)
	local elapsed = now - lookStartedAt
	local total = GrandpaConfig.LookAroundDuration * GrandpaConfig.LookAroundSweeps
	if elapsed >= total then
		say("Показалось… Спать, спать.", 3)
		awareness = 0
		startReturn()
		return
	end
	local phase = elapsed / GrandpaConfig.LookAroundDuration * math.pi * 2
	local yaw = math.rad(math.sin(phase) * GrandpaConfig.LookAroundAngle)
	local rotation = lookBaseCFrame.Rotation * CFrame.Angles(0, yaw, 0)
	root.CFrame = CFrame.new(root.Position) * rotation
end

local function tickSearching(dt, now)
	if updateVision(dt, GrandpaConfig.DetectRateSearching, now) then
		return
	end
	if lookStartedAt then
		tickLookAround(now)
		return
	end
	if followPath() or now >= deadline then
		lookStartedAt = now
		lookBaseCFrame = root.CFrame
		humanoid.AutoRotate = false
		humanoid:MoveTo(root.Position)
	end
end

local function tickChasing(now)
	local target = chaseTarget
	local character = target and target.Parent and target.Character
	local targetRoot = character and character:FindFirstChild("HumanoidRootPart")
	if not targetRoot then
		startSearch(lastSeenPosition)
		return
	end

	local visible = canSee(target)
	if visible then
		lastSeenAt = now
		lastSeenPosition = targetRoot.Position
	elseif now - lastSeenAt > GrandpaConfig.LoseSightTime then
		startSearch(lastSeenPosition)
		return
	end

	local catchDistance = ElfService.IsHidden(target) and HIDDEN_CATCH_DISTANCE or CATCH_DISTANCE
	if flatDistance(targetRoot.Position, root.Position) <= catchDistance then
		catch(target)
		return
	end

	if visible then
		humanoid:MoveTo(targetRoot.Position)
	else
		if now >= nextRepathAt then
			nextRepathAt = now + 0.5
			pathTo(lastSeenPosition)
		end
		followPath()
	end
end

local function tickReturning(dt, now)
	if updateVision(dt, GrandpaConfig.DetectRateSearching, now) then
		return
	end
	if followPath() or now >= deadline then
		awareness = 0
		detect = 0
		setState("Asleep")
	end
end

local function step(dt, now)
	if heardClearAt > 0 and now >= heardClearAt then
		heardClearAt = 0
		heardLabel.Text = ""
	end
	if not active then
		return
	end
	-- In a spooky house he sleeps lighter: awareness fades more slowly.
	if spooky then
		dt *= GrandpaConfig.SpookyDecayMultiplier
	end

	if state == "Asleep" then
		awareness = math.max(0, awareness - GrandpaConfig.DecayAsleep * dt)
		if awareness >= GrandpaConfig.StirThreshold then
			setState("Stirring")
		end
	elseif state == "Stirring" then
		awareness = math.max(0, awareness - GrandpaConfig.DecayStirring * dt)
		if awareness >= GrandpaConfig.SuspiciousThreshold then
			setState("Suspicious")
		elseif awareness < GrandpaConfig.StirThreshold - GrandpaConfig.Hysteresis then
			setState("Asleep")
		end
	elseif state == "Suspicious" then
		awareness = math.max(0, awareness - GrandpaConfig.DecaySuspicious * dt)
		if updateVision(dt, GrandpaConfig.DetectRateSuspicious, now) then
			return
		end
		if awareness >= GrandpaConfig.SearchThreshold then
			startSearch(noiseTarget)
		elseif awareness < GrandpaConfig.SuspiciousThreshold - GrandpaConfig.Hysteresis then
			setState("Stirring")
		end
	elseif state == "Searching" then
		tickSearching(dt, now)
	elseif state == "Chasing" then
		tickChasing(now)
	elseif state == "Returning" then
		tickReturning(dt, now)
	end
end

local function onNoise(event)
	if not active then
		return
	end
	local heard = NoiseService.LoudnessAt(event, root.Position)
	if spooky then
		heard *= GrandpaConfig.SpookyHearingMultiplier
	end
	local awake = state == "Searching" or state == "Chasing" or state == "Returning"
	if awake then
		heard *= GrandpaConfig.AwakeHearingMultiplier
	end
	if heard < GrandpaConfig.HearingFloor then
		return
	end

	noiseTarget = event.Position
	if state ~= "Chasing" then
		say(HEARD_WORDS[event.Kind] or "шум?", 2)
	end

	if awake then
		if state ~= "Chasing" and heard >= GrandpaConfig.ReinvestigateLoudness then
			startSearch(event.Position)
		end
		return
	end

	awareness = math.min(100, awareness + heard * GrandpaConfig.AwarenessGain)
	if heard >= Config.Noise.WakeThreshold then
		awareness = math.max(awareness, GrandpaConfig.SearchThreshold)
		startSearch(event.Position)
	end
end

function GrandpaService.GetState()
	return state
end

-- Grandpa only listens and moves while a crew is inside the house.
function GrandpaService.SetActive(value)
	active = value
end

function GrandpaService.SetSpooky(value)
	spooky = value
end

function GrandpaService.Reset()
	awareness = 0
	detect = 0
	chaseTarget = nil
	noiseTarget = nil
	destination = nil
	waypoints = nil
	heardLabel.Text = ""
	if state == "Asleep" then
		getIntoBed(house.BedLieCFrame)
	else
		setState("Asleep")
	end
end

function GrandpaService.Start(world)
	house = world.House
	buildRig()

	local indicator = INDICATOR.Asleep
	indicatorLabel.Text = indicator.Text
	indicatorLabel.TextColor3 = indicator.Color

	NoiseService.NoiseEmitted:Connect(onNoise)

	task.spawn(function()
		while true do
			local dt = task.wait(GrandpaConfig.TickInterval)
			local ok, message = pcall(step, dt, os.clock())
			if not ok then
				warn("[Grandpa] step failed: " .. tostring(message))
			end
		end
	end)
end

return GrandpaService
