-- The Fat Cat: sleeps, wanders between his bed and the bowl, investigates noises, meows at elves
-- and yowls (waking Grandpa) when someone steps on his tail without sneaking.
-- Catnip (thrown with F) keeps him purring and harmless for a while.

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared.Config)
local Remotes = require(Shared.Remotes)
local Signal = require(Shared.Signal)

local NoiseService = require(script.Parent.NoiseService)
local ElfService = require(script.Parent.ElfService)
local ProfileService = require(script.Parent.ProfileService)
local PathAgent = require(script.Parent.PathAgent)

local CatConfig = Config.Cat
local CATNIP_RADIUS = Config.Catnip.RadiusMeters * Config.StudsPerMeter
local ROOT_HALF_HEIGHT = 0.7
local OWN_NOISES = { Cat = true, Meow = true }

local CatService = {}

-- Fires with (player) when the cat yowls because of that elf.
CatService.Yowled = Signal.new()

local house
local model, root, humanoid, label, agent
local active = false
local state = "Sleeping" -- Sleeping | Walking | Idle | Investigating | Fleeing | Purring
local stateUntil = 0
local walkGoal -- "Bed" | "Bowl"
local nextTailAt = 0
local nextMeowAt = 0
local pouch

local function setLabel(text)
	label.Text = text
end

local function makePart(name, size, color, shape)
	local part = Instance.new("Part")
	part.Name = name
	part.Size = size
	part.Color = color
	part.Material = Enum.Material.Fabric
	part.TopSurface = Enum.SurfaceType.Smooth
	part.BottomSurface = Enum.SurfaceType.Smooth
	part.CanCollide = false
	part.Massless = true
	if shape then
		part.Shape = shape
	end
	return part
end

local function weldTo(part, offset)
	part.CFrame = root.CFrame * offset
	local weld = Instance.new("WeldConstraint")
	weld.Part0 = root
	weld.Part1 = part
	weld.Parent = part
	part.Parent = model
end

local function restingCFrame(position)
	return CFrame.new(position.X, house.FloorY + ROOT_HALF_HEIGHT + 0.3, position.Z)
end

local function buildRig()
	model = Instance.new("Model")
	model.Name = "FatCat"

	local orange = Color3.fromRGB(230, 140, 60)
	root = makePart("HumanoidRootPart", Vector3.new(2.2, ROOT_HALF_HEIGHT * 2, 3.2), orange)
	root.Massless = false
	root.Anchored = true
	root.CFrame = restingCFrame(house.CatBedPosition)
	root.Parent = model
	model.PrimaryPart = root

	local head = makePart("Head", Vector3.new(1.5, 1.5, 1.5), orange, Enum.PartType.Ball)
	weldTo(head, CFrame.new(0, 0.5, -1.9))
	weldTo(makePart("Belly", Vector3.new(1.6, 0.4, 2.4), Color3.fromRGB(250, 240, 225)), CFrame.new(0, -0.6, 0))
	weldTo(makePart("Tail", Vector3.new(0.4, 0.4, 2.2), orange), CFrame.new(0, 0.3, 2.5))
	weldTo(makePart("Collar", Vector3.new(1.3, 0.3, 0.3), Color3.fromRGB(200, 40, 50)), CFrame.new(0, 0.1, -1.3))

	humanoid = Instance.new("Humanoid")
	humanoid.RigType = Enum.HumanoidRigType.R15
	humanoid.HipHeight = CatConfig.HipHeight
	humanoid.RequiresNeck = false
	humanoid.BreakJointsOnDeath = false
	humanoid.DisplayDistanceType = Enum.HumanoidDisplayDistanceType.None
	humanoid.WalkSpeed = CatConfig.WalkSpeed
	humanoid.Parent = model

	local billboard = Instance.new("BillboardGui")
	billboard.Adornee = head
	billboard.Size = UDim2.fromOffset(110, 44)
	billboard.StudsOffset = Vector3.new(0, 2.2, 0)
	billboard.AlwaysOnTop = true
	billboard.Parent = head

	label = Instance.new("TextLabel")
	label.BackgroundTransparency = 1
	label.Size = UDim2.fromScale(1, 1)
	label.Font = Enum.Font.FredokaOne
	label.TextScaled = true
	label.TextColor3 = Color3.fromRGB(255, 220, 160)
	label.TextStrokeTransparency = 0.3
	label.Text = "Zzz"
	label.Parent = billboard

	model.Parent = workspace

	agent = PathAgent.new(humanoid, root, {
		FloorY = house.FloorY,
		ArriveDistance = 2.5,
		AgentRadius = 1.5,
		AgentHeight = 3,
	})
end

local function wakeUp()
	if root.Anchored then
		root.Anchored = false
		pcall(function()
			root:SetNetworkOwner(nil)
		end)
	end
end

local function sleepAt(position, now)
	agent:Stop()
	root.Anchored = true
	model:PivotTo(restingCFrame(position))
	state = "Sleeping"
	stateUntil = now + math.random(CatConfig.SleepMin, CatConfig.SleepMax)
	setLabel("Zzz")
end

local function idle(now, text)
	agent:Stop()
	state = "Idle"
	stateUntil = now + math.random(CatConfig.IdleMin, CatConfig.IdleMax)
	setLabel(text or "")
end

local function walkTo(goalName)
	wakeUp()
	walkGoal = goalName
	state = "Walking"
	humanoid.WalkSpeed = CatConfig.WalkSpeed
	setLabel("")
	agent:SetDestination(goalName == "Bed" and house.CatBedPosition or house.CatBowlPosition)
end

local function clearPouch()
	if pouch then
		pouch:Destroy()
		pouch = nil
	end
end

local function nearestElf()
	local best, bestDistance
	for _, player in ipairs(Players:GetPlayers()) do
		local character = player.Character
		local elfRoot = character and character:FindFirstChild("HumanoidRootPart")
		if elfRoot and not ElfService.IsDown(player) then
			local offset = elfRoot.Position - root.Position
			if math.abs(offset.Y) < 6 then
				local distance = Vector3.new(offset.X, 0, offset.Z).Magnitude
				if not bestDistance or distance < bestDistance then
					best, bestDistance = player, distance
				end
			end
		end
	end
	return best, bestDistance
end

local function checkElves(now)
	if state == "Purring" or state == "Fleeing" then
		return
	end
	local player, distance = nearestElf()
	if not player then
		return
	end

	if distance <= CatConfig.TailRadius and not ElfService.IsCrouching(player) and now >= nextTailAt then
		nextTailAt = now + CatConfig.TailCooldown
		NoiseService.Emit(root.Position, Config.Noise.CatYowl, "Cat", nil)
		Remotes.Toast:FireAllClients("МЯЯЯУ! Кому-то наступили на хвост.")
		CatService.Yowled:Fire(player)

		wakeUp()
		local away = root.Position - player.Character.HumanoidRootPart.Position
		away = Vector3.new(away.X, 0, away.Z)
		if away.Magnitude < 0.1 then
			away = Vector3.new(1, 0, 0)
		end
		state = "Fleeing"
		stateUntil = now + 3
		humanoid.WalkSpeed = CatConfig.FleeSpeed
		setLabel("!!")
		agent:SetDestination(root.Position + away.Unit * CatConfig.FleeDistance)
		return
	end

	if state ~= "Sleeping" and distance <= CatConfig.MeowRadius and now >= nextMeowAt then
		nextMeowAt = now + CatConfig.MeowCooldown
		NoiseService.Emit(root.Position, Config.Noise.CatMeow, "Meow", nil)
		setLabel("мяу")
	end
end

local function step(now)
	if not active then
		return
	end
	checkElves(now)

	if state == "Sleeping" then
		if now >= stateUntil then
			walkTo("Bowl")
		end
	elseif state == "Walking" then
		if agent:Update() then
			if walkGoal == "Bed" then
				sleepAt(house.CatBedPosition, now)
			else
				idle(now, "хрум")
				walkGoal = "Bowl"
			end
		end
	elseif state == "Idle" then
		if now >= stateUntil then
			walkTo("Bed")
		end
	elseif state == "Investigating" then
		if agent:Update() or now >= stateUntil then
			idle(now, "?")
		end
	elseif state == "Fleeing" then
		if agent:Update() or now >= stateUntil then
			humanoid.WalkSpeed = CatConfig.WalkSpeed
			idle(now, "")
		end
	elseif state == "Purring" then
		agent:Update()
		if now >= stateUntil then
			clearPouch()
			idle(now, "")
		end
	end
end

local function onNoise(event)
	if not active or OWN_NOISES[event.Kind] then
		return
	end
	if state == "Purring" or state == "Fleeing" or state == "Investigating" then
		return
	end
	if NoiseService.LoudnessAt(event, root.Position) < CatConfig.HearThreshold then
		return
	end
	wakeUp()
	state = "Investigating"
	stateUntil = os.clock() + 12
	humanoid.WalkSpeed = CatConfig.WalkSpeed
	setLabel("?")
	agent:SetDestination(event.Position)
end

local function throwCatnip(player)
	if not active then
		return
	end
	local character = player.Character
	local elfRoot = character and character:FindFirstChild("HumanoidRootPart")
	if not elfRoot or ElfService.IsDown(player) then
		return
	end
	if not ProfileService.UseCatnip(player) then
		Remotes.Toast:FireClient(player, "Мяты нет. Купите на верстаке.")
		return
	end

	clearPouch()
	local landing = (elfRoot.CFrame * CFrame.new(0, 0, -8)).Position
	pouch = Instance.new("Part")
	pouch.Name = "CatnipPouch"
	pouch.Size = Vector3.new(1, 0.6, 1)
	pouch.Color = Color3.fromRGB(110, 190, 90)
	pouch.Material = Enum.Material.Fabric
	pouch.Anchored = true
	pouch.CanCollide = false
	pouch.Position = Vector3.new(landing.X, house.FloorY + 0.3, landing.Z)
	pouch.Parent = workspace

	if (pouch.Position - root.Position).Magnitude > CATNIP_RADIUS then
		Remotes.Toast:FireClient(player, "Кот слишком далеко и мяту не учуял.")
		return
	end

	wakeUp()
	state = "Purring"
	stateUntil = os.clock() + Config.Catnip.Duration
	humanoid.WalkSpeed = CatConfig.WalkSpeed
	setLabel("мрр")
	Remotes.Toast:FireClient(player, "Кот занят мятой на 20 секунд.")
	agent:SetDestination(pouch.Position)
end

function CatService.SetActive(value)
	active = value
end

function CatService.Reset()
	clearPouch()
	nextTailAt = 0
	nextMeowAt = 0
	sleepAt(house.CatBedPosition, os.clock())
end

function CatService.Start(world)
	house = world.House
	buildRig()

	NoiseService.NoiseEmitted:Connect(onNoise)
	Remotes.ThrowCatnip.OnServerEvent:Connect(throwCatnip)

	task.spawn(function()
		while true do
			task.wait(CatConfig.TickInterval)
			local ok, message = pcall(step, os.clock())
			if not ok then
				warn("[Cat] step failed: " .. tostring(message))
			end
		end
	end)
end

return CatService
