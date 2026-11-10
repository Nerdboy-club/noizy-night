-- Gifts of every kind: pick up / put down (E), sack (R), deliver under the tree, co-carry the bicycle.
--   Small    fits into the sack
--   Noisy    wind-up toy, ticks unless it is in the sack
--   Huge     wrapped bicycle: hands only, slows the carrier, two elves carry it almost at full speed
--   Fragile  snow globe: shatters on a hard impact (worth nothing afterwards)
--   MusicBox alarm box: rings when its spring runs down; picking it up rewinds the key
--   RcCar    left on the floor it drives off buzzing and distracts Grandpa
-- Every gift has a wrapping Integrity (0..100) that scales the tips.

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared.Config)
local Remotes = require(Shared.Remotes)
local Signal = require(Shared.Signal)

local NoiseService = require(script.Parent.NoiseService)
local ElfService = require(script.Parent.ElfService)
local ProfileService = require(script.Parent.ProfileService)

local GiftConfig = Config.Gift
local TICK = 0.1

local GiftService = {}

-- Fires with (gift, player) when a gift is placed under the tree.
GiftService.Delivered = Signal.new()
-- Fires with (gift) when a snow globe shatters.
GiftService.Broken = Signal.new()

local gifts = {}
local holding = {} -- [player] = gift in hands
local partnerOf = {} -- [player] = bicycle the player is helping to carry
local sacks = {} -- [player] = { gift, ... }
local deliveryZone -- { Position: Vector3, Radius: number } or nil
local fallbackCFrame = CFrame.new(0, 10, 0)
local nextId = 0

local function toast(player, text)
	Remotes.Toast:FireClient(player, text)
end

local function getRoot(player)
	local character = player.Character
	return character and character:FindFirstChild("HumanoidRootPart")
end

local function sackOf(player)
	local sack = sacks[player]
	if not sack then
		sack = {}
		sacks[player] = sack
	end
	return sack
end

local function publish(player)
	if not player.Parent then
		return
	end
	local gift = holding[player] or partnerOf[player]
	player:SetAttribute("Holding", gift and gift.Kind or "")
	player:SetAttribute("SackCount", #sackOf(player))
	player:SetAttribute("SackCap", 1 + ProfileService.GetLevel(player, "Sack"))
end

local function claimForServer(gift)
	-- Loose gifts are simulated by the server so impact damage cannot be faked.
	pcall(function()
		gift.Part:SetNetworkOwner(nil)
	end)
end

local function setIntegrity(gift, value)
	gift.Integrity = math.clamp(value, 0, 100)
	gift.Part:SetAttribute("Integrity", math.floor(gift.Integrity + 0.5))
end

local function updatePromptText(gift)
	if gift.State == "Held" then
		gift.Prompt.ActionText = gift.Kind == "Huge" and "Положить / нести вдвоём" or "Положить"
	else
		gift.Prompt.ActionText = "Взять"
	end
end

local function soloSpeed(player)
	local grip = ProfileService.GetLevel(player, "Grip")
	return math.min(1, GiftConfig.HugeSoloSpeed + GiftConfig.HugeGripBonusPerLevel * grip)
end

local function releasePartner(gift)
	local partner = gift.Partner
	if not partner then
		return
	end
	gift.Partner = nil
	partnerOf[partner] = nil
	for _, instance in ipairs(gift.RopeParts) do
		instance:Destroy()
	end
	gift.RopeParts = {}
	ElfService.SetSpeedMultiplier(partner, 1)
	if gift.Holder then
		ElfService.SetSpeedMultiplier(gift.Holder, soloSpeed(gift.Holder))
	end
	publish(partner)
end

local function attachPartner(gift, player)
	local root = getRoot(player)
	if not root then
		return
	end

	-- The carrier holds the back end; the partner grabs the front end on a short rope.
	local giftAttachment = Instance.new("Attachment")
	giftAttachment.Name = "CoCarryGrip"
	giftAttachment.Position = Vector3.new(0, 0, -gift.Part.Size.Z / 2)
	giftAttachment.Parent = gift.Part

	local rootAttachment = Instance.new("Attachment")
	rootAttachment.Name = "CoCarryHands"
	rootAttachment.Parent = root

	local rope = Instance.new("RopeConstraint")
	rope.Attachment0 = giftAttachment
	rope.Attachment1 = rootAttachment
	rope.Length = GiftConfig.CoCarryRopeLength
	rope.Visible = false
	rope.Parent = gift.Part

	gift.RopeParts = { giftAttachment, rootAttachment, rope }
	gift.Partner = player
	partnerOf[player] = gift
	ElfService.SetSpeedMultiplier(player, GiftConfig.HugeDuoSpeed)
	ElfService.SetSpeedMultiplier(gift.Holder, GiftConfig.HugeDuoSpeed)
	publish(player)
	toast(player, "Несём вместе!")
	toast(gift.Holder, "Несём вместе!")
end

-- Takes the gift out of the carrier's hands and leaves it loose where it is.
local function detach(gift)
	releasePartner(gift)
	if gift.Weld then
		gift.Weld:Destroy()
		gift.Weld = nil
	end
	local holder = gift.Holder
	gift.Holder = nil
	gift.State = "Loose"
	gift.Part.CanCollide = true
	gift.Part.CanQuery = true
	gift.Part.Massless = false
	updatePromptText(gift)
	if holder then
		holding[holder] = nil
		ElfService.SetSpeedMultiplier(holder, 1)
		publish(holder)
	end
end

local function pickUp(player, gift)
	local root = getRoot(player)
	if not root or ElfService.IsDown(player) then
		return
	end
	if holding[player] or partnerOf[player] then
		toast(player, "Руки заняты.")
		return
	end

	local part = gift.Part
	local huge = gift.Kind == "Huge"
	part.Anchored = false
	part.CanCollide = huge -- the bicycle really bumps into door frames
	part.CanQuery = false
	part.Massless = true
	part.AssemblyLinearVelocity = Vector3.zero
	part.AssemblyAngularVelocity = Vector3.zero
	if huge then
		part.CFrame = root.CFrame * CFrame.new(0, 0.5, -(1.5 + part.Size.Z / 2))
	else
		part.CFrame = root.CFrame * CFrame.new(0, 0, -2)
	end

	local weld = Instance.new("WeldConstraint")
	weld.Name = "CarryWeld"
	weld.Part0 = root
	weld.Part1 = part
	weld.Parent = part

	gift.Weld = weld
	gift.Holder = player
	gift.State = "Held"
	gift.DriveUntil = nil
	gift.DriveStartAt = nil
	if gift.Kind == "MusicBox" then
		gift.Wind = GiftConfig.MusicBoxWindTime
		gift.RingsLeft = 0
	end
	holding[player] = gift
	updatePromptText(gift)

	if huge then
		ElfService.SetSpeedMultiplier(player, soloSpeed(player))
	end
	publish(player)
end

local function isInDeliveryZone(position)
	if not deliveryZone then
		return false
	end
	local offset = position - deliveryZone.Position
	return Vector3.new(offset.X, 0, offset.Z).Magnitude <= deliveryZone.Radius
end

local function place(player, gift)
	local root = getRoot(player)
	if not root then
		return
	end
	local part = gift.Part
	local reach = gift.Kind == "Huge" and (2 + part.Size.Z / 2) or 3
	local front = (root.CFrame * CFrame.new(0, 0, -reach)).Position

	detach(gift)

	-- Find the floor under the drop point (floors differ between hub, rooftops and house).
	local params = RaycastParams.new()
	params.FilterType = Enum.RaycastFilterType.Exclude
	params.FilterDescendantsInstances = { player.Character, part }
	params.RespectCanCollide = true
	local hit = workspace:Raycast(front + Vector3.new(0, 3, 0), Vector3.new(0, -12, 0), params)
	local floorY = hit and hit.Position.Y or (root.Position.Y - 3)

	local _, yaw = root.CFrame:ToOrientation()
	part.CFrame = CFrame.new(front.X, floorY + part.Size.Y / 2 + 0.05, front.Z) * CFrame.Angles(0, yaw, 0)
	part.AssemblyLinearVelocity = Vector3.zero
	part.AssemblyAngularVelocity = Vector3.zero

	if isInDeliveryZone(part.Position) then
		gift.State = "Delivered"
		part.Anchored = true
		gift.Prompt.Enabled = false
		GiftService.Delivered:Fire(gift, player)
		return
	end

	claimForServer(gift)
	if gift.Kind == "RcCar" and deliveryZone and gift.Charges > 0 then
		gift.DriveStartAt = os.clock() + GiftConfig.RcCarDelay
	end
end

local function dropFromFall(player)
	local carried = partnerOf[player]
	if carried then
		releasePartner(carried)
	end

	local gift = holding[player]
	if not gift then
		return
	end
	local root = getRoot(player)
	detach(gift)
	if root then
		gift.Part.AssemblyLinearVelocity = root.AssemblyLinearVelocity
			+ root.CFrame.LookVector * 10
			+ Vector3.new(0, 14, 0)
	end
	if gift.Kind ~= "Fragile" then
		setIntegrity(gift, gift.Integrity - GiftConfig.FallDropDamage)
	end
	claimForServer(gift)
end

local function onTouched(gift)
	if gift.State ~= "Loose" then
		return
	end
	local now = os.clock()
	local part = gift.Part
	local speed = part.AssemblyLinearVelocity.Magnitude

	if gift.Kind == "Fragile" then
		if not gift.Broken and now >= gift.GraceUntil and speed >= GiftConfig.FragileBreakSpeed then
			gift.Broken = true
			setIntegrity(gift, 0)
			part.Color = Color3.fromRGB(120, 130, 140)
			part.Transparency = 0.4
			NoiseService.Emit(part.Position, Config.Noise.GlassBreak, "Glass", nil)
			GiftService.Broken:Fire(gift)
		end
		return
	end

	if speed < GiftConfig.ImpactMinSpeed or now < gift.NextImpactAt then
		return
	end
	gift.NextImpactAt = now + GiftConfig.ImpactCooldown
	local damage = math.clamp(
		(speed - GiftConfig.ImpactMinSpeed) * GiftConfig.ImpactDamagePerSpeed,
		GiftConfig.ImpactMinDamage,
		GiftConfig.ImpactMaxDamage
	)
	setIntegrity(gift, gift.Integrity - damage)
	NoiseService.Emit(part.Position, Config.Noise.GiftImpact, "Gift", nil)
end

local function onPromptTriggered(gift, player)
	if gift.State == "Loose" then
		pickUp(player, gift)
	elseif gift.State == "Held" then
		if gift.Holder == player then
			place(player, gift)
		elseif gift.Kind == "Huge" then
			if gift.Partner == player then
				releasePartner(gift)
			elseif gift.Partner == nil and not holding[player] and not ElfService.IsDown(player) then
				attachPartner(gift, player)
			end
		end
	end
end

local function toggleSack(player)
	local sack = sackOf(player)
	local gift = holding[player]

	if gift then
		if not gift.Def.Sackable then
			return toast(player, gift.Def.Name .. " не влезает в мешок.")
		end
		if #sack >= 1 + ProfileService.GetLevel(player, "Sack") then
			return toast(player, "Мешок полон.")
		end
		detach(gift)
		gift.State = "Sack"
		gift.Part.Parent = nil
		table.insert(sack, gift)
		publish(player)
		return
	end

	if #sack == 0 or partnerOf[player] or ElfService.IsDown(player) then
		return
	end
	gift = table.remove(sack)
	gift.State = "Loose"
	gift.Part.Parent = workspace
	pickUp(player, gift)
	publish(player)
end

-- Puts every gift from the player's sack on the ground at `cframe`.
local function emptySack(player, cframe)
	local sack = sackOf(player)
	for index, gift in ipairs(sack) do
		gift.State = "Loose"
		gift.Part.Anchored = false
		gift.Part.CFrame = cframe * CFrame.new(index * 2.5 - 2.5, 2, -3)
		gift.Part.AssemblyLinearVelocity = Vector3.zero
		gift.Part.Parent = workspace
		updatePromptText(gift)
		claimForServer(gift)
	end
	table.clear(sack)
end

local function updateGift(gift, dt, now)
	local part = gift.Part
	local state = gift.State

	if state == "Held" then
		local weld = gift.Weld
		if weld == nil or weld.Part0 == nil or not weld.Part0:IsDescendantOf(workspace) then
			detach(gift)
			claimForServer(gift)
		end
	elseif state == "Loose" and part.Position.Y < fallbackCFrame.Position.Y - 50 then
		part.AssemblyLinearVelocity = Vector3.zero
		part.CFrame = fallbackCFrame * CFrame.new(0, 2, -4)
	end

	if gift.Partner then
		local partnerRoot = getRoot(gift.Partner)
		if not partnerRoot then
			releasePartner(gift)
		end
	end

	local audible = state == "Loose" or state == "Held"

	if gift.Kind == "Noisy" and audible and now >= gift.NextTickAt then
		gift.NextTickAt = now + GiftConfig.NoisyTickInterval
		NoiseService.Emit(part.Position, Config.Noise.NoisyTick, "Tick", nil)
	end

	if gift.Kind == "MusicBox" and state == "Loose" then
		if gift.RingsLeft > 0 then
			if now >= gift.NextTickAt then
				gift.NextTickAt = now + GiftConfig.MusicBoxRingInterval
				gift.RingsLeft -= 1
				NoiseService.Emit(part.Position, Config.Noise.MusicBox, "Alarm", nil)
				if gift.RingsLeft == 0 then
					gift.Wind = GiftConfig.MusicBoxWindTime
				end
			end
		else
			gift.Wind -= dt
			if gift.Wind <= 0 then
				gift.RingsLeft = GiftConfig.MusicBoxRings
				gift.NextTickAt = now
			end
		end
	end

	if gift.Kind == "RcCar" and state == "Loose" then
		if gift.DriveStartAt and now >= gift.DriveStartAt then
			gift.DriveStartAt = nil
			gift.Charges -= 1
			gift.DriveUntil = now + GiftConfig.RcCarDriveTime
			gift.NextTickAt = now
		end
		if gift.DriveUntil then
			if now >= gift.DriveUntil then
				gift.DriveUntil = nil
				part.AssemblyLinearVelocity = Vector3.zero
			else
				local look = part.CFrame.LookVector
				local forward = Vector3.new(look.X, 0, look.Z)
				if forward.Magnitude < 0.1 then
					forward = Vector3.new(0, 0, -1)
				end
				forward = forward.Unit

				local params = RaycastParams.new()
				params.FilterType = Enum.RaycastFilterType.Exclude
				params.FilterDescendantsInstances = { part }
				params.RespectCanCollide = true
				if workspace:Raycast(part.Position, forward * 3.5, params) then
					-- Turn around at walls.
					local turn = math.rad(math.random(110, 250))
					part.CFrame = CFrame.new(part.Position) * CFrame.Angles(0, turn, 0) * part.CFrame.Rotation
					part.AssemblyAngularVelocity = Vector3.zero
				else
					local fall = part.AssemblyLinearVelocity.Y
					part.AssemblyLinearVelocity = forward * GiftConfig.RcCarSpeed + Vector3.new(0, fall, 0)
				end

				if now >= gift.NextTickAt then
					gift.NextTickAt = now + GiftConfig.RcCarBuzzInterval
					NoiseService.Emit(part.Position, Config.Noise.RcCar, "Buzz", nil)
				end
			end
		end
	end
end

function GiftService.Spawn(kind, cframe)
	local def = Config.GiftKinds[kind]
	if not def then
		return nil
	end

	local part = Instance.new("Part")
	part.Name = "Gift" .. kind
	part.Size = def.Size
	part.Color = def.Color
	part.Material = Enum.Material.Fabric
	part.TopSurface = Enum.SurfaceType.Smooth
	part.BottomSurface = Enum.SurfaceType.Smooth
	if def.Ball then
		part.Shape = Enum.PartType.Ball
		part.Material = Enum.Material.Glass
		part.Transparency = 0.15
	end
	part.CFrame = cframe

	if not def.Ball then
		local ribbon = Instance.new("Part")
		ribbon.Name = "Ribbon"
		ribbon.Size = Vector3.new(def.Size.X + 0.1, def.Size.Y + 0.1, 0.5)
		ribbon.Color = Color3.fromRGB(250, 240, 220)
		ribbon.Material = Enum.Material.Fabric
		ribbon.CanCollide = false
		ribbon.CanQuery = false
		ribbon.CanTouch = false
		ribbon.Massless = true
		ribbon.CFrame = cframe
		local ribbonWeld = Instance.new("WeldConstraint")
		ribbonWeld.Part0 = part
		ribbonWeld.Part1 = ribbon
		ribbonWeld.Parent = ribbon
		ribbon.Parent = part
	end

	local prompt = Instance.new("ProximityPrompt")
	prompt.ObjectText = def.Name
	prompt.ActionText = "Взять"
	prompt.KeyboardKeyCode = Enum.KeyCode.E
	prompt.MaxActivationDistance = kind == "Huge" and 11 or 8
	prompt.RequiresLineOfSight = false
	prompt.Parent = part

	nextId += 1
	local now = os.clock()
	local gift = {
		Id = nextId,
		Kind = kind,
		Def = def,
		Part = part,
		Prompt = prompt,
		State = "Loose",
		Holder = nil,
		Partner = nil,
		Weld = nil,
		RopeParts = {},
		Integrity = 100,
		Broken = false,
		GraceUntil = now + 1.5, -- settling after spawn must not shatter a snow globe
		NextImpactAt = now + 1,
		NextTickAt = now + 1,
		Wind = GiftConfig.MusicBoxWindTime,
		RingsLeft = 0,
		Charges = GiftConfig.RcCarCharges,
		DriveStartAt = nil,
		DriveUntil = nil,
	}
	part:SetAttribute("Integrity", 100)
	table.insert(gifts, gift)

	prompt.Triggered:Connect(function(player)
		onPromptTriggered(gift, player)
	end)
	part.Touched:Connect(function()
		onTouched(gift)
	end)

	part.Parent = workspace
	claimForServer(gift)
	return gift
end

function GiftService.GetAll()
	return gifts
end

-- Where gifts may be delivered: (position, radius), or nil for "nowhere" (hub, rooftops).
function GiftService.SetDeliveryZone(position, radius)
	deliveryZone = position and { Position = position, Radius = radius } or nil
end

-- Where lost gifts (fell out of the world, owner left) reappear.
function GiftService.SetFallback(cframe)
	fallbackCFrame = cframe
end

-- Takes everything the player carries (hands + sack) and returns it as a list of loose gifts.
function GiftService.CollectCarried(player)
	local list = {}
	local gift = holding[player]
	if gift then
		detach(gift)
		table.insert(list, gift)
	end
	local sack = sackOf(player)
	for _, stashed in ipairs(sack) do
		stashed.State = "Loose"
		stashed.Part.Parent = workspace
		table.insert(list, stashed)
	end
	table.clear(sack)
	publish(player)
	return list
end

-- Freezes a gift somewhere as scenery (loaded onto the sleigh).
function GiftService.Park(gift, cframe)
	gift.State = "Parked"
	gift.Prompt.Enabled = false
	gift.Part.Anchored = true
	gift.Part.CanCollide = false
	gift.Part.AssemblyLinearVelocity = Vector3.zero
	gift.Part.CFrame = cframe
end

function GiftService.Clear()
	for _, gift in ipairs(gifts) do
		for _, instance in ipairs(gift.RopeParts) do
			instance:Destroy()
		end
		gift.Part:Destroy()
	end
	table.clear(gifts)
	for player in pairs(holding) do
		ElfService.SetSpeedMultiplier(player, 1)
	end
	for player in pairs(partnerOf) do
		ElfService.SetSpeedMultiplier(player, 1)
	end
	table.clear(holding)
	table.clear(partnerOf)
	table.clear(sacks)
	for _, player in ipairs(Players:GetPlayers()) do
		publish(player)
	end
end

function GiftService.Start()
	ElfService.Fell:Connect(dropFromFall)
	ElfService.SetBonkExempt(function(a, b)
		return (partnerOf[a] ~= nil and partnerOf[a].Holder == b) or (partnerOf[b] ~= nil and partnerOf[b].Holder == a)
	end)

	Remotes.SackToggle.OnServerEvent:Connect(toggleSack)

	Players.PlayerAdded:Connect(publish)
	for _, player in ipairs(Players:GetPlayers()) do
		publish(player)
	end

	Players.PlayerRemoving:Connect(function(player)
		local root = getRoot(player)
		local gift = holding[player]
		if gift then
			detach(gift)
			claimForServer(gift)
		end
		local carried = partnerOf[player]
		if carried then
			releasePartner(carried)
		end
		emptySack(player, root and root.CFrame or fallbackCFrame)
		holding[player] = nil
		partnerOf[player] = nil
		sacks[player] = nil
	end)

	task.spawn(function()
		while true do
			local dt = task.wait(TICK)
			local now = os.clock()
			for _, gift in ipairs(table.clone(gifts)) do
				if gift.Part.Parent then
					local ok, message = pcall(updateGift, gift, dt, now)
					if not ok then
						warn("[Gift] update failed: " .. tostring(message))
					end
				end
			end
		end
	end)
end

return GiftService
