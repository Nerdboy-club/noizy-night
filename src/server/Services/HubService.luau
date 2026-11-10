-- Santa's workshop stations: order board, conveyor, sleigh, workbench/shop, door to the lobby.

local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared.Config)
local Remotes = require(Shared.Remotes)

local GiftService = require(script.Parent.GiftService)
local MissionService = require(script.Parent.MissionService)
local OrderService = require(script.Parent.OrderService)

local HubService = {}

local COLOR_CARD = Color3.fromRGB(245, 235, 210)
local COLOR_CARD_PICKED = Color3.fromRGB(190, 235, 170)
local COLOR_CARD_DIM = Color3.fromRGB(150, 145, 135)
local CONVEYOR_INTERVAL = 1.8

local hub
local loaded = 0
local conveyorRun = 0

local function toast(player, text)
	Remotes.Toast:FireClient(player, text)
end

local function refreshBoard(orders, selectedIndex)
	for index, card in ipairs(hub.Cards) do
		local order = orders[index]
		card.Label.Text = order and OrderService.Describe(order) or ""
		card.Prompt.Enabled = order ~= nil and selectedIndex == nil
		if selectedIndex == nil then
			card.Part.Color = COLOR_CARD
		else
			card.Part.Color = index == selectedIndex and COLOR_CARD_PICKED or COLOR_CARD_DIM
		end
	end
end

local function refreshSleigh()
	local order = MissionService.GetOrder()
	if not order then
		hub.SleighLabel.Text = "Сани"
		hub.SleighPrompt.ActionText = "Загрузить подарки"
		return
	end
	hub.SleighLabel.Text = string.format("Сани: %d/%d", loaded, #order.Kinds)
	hub.SleighPrompt.ActionText = loaded >= #order.Kinds and "В путь!" or "Загрузить подарки"
end

-- Gifts for the chosen order roll out of the hatch one by one.
local function runConveyor(order)
	conveyorRun += 1
	local run = conveyorRun
	for _, kind in ipairs(order.Kinds) do
		if run ~= conveyorRun then
			return
		end
		local size = Config.GiftKinds[kind].Size
		-- Spawn resting on the belt so fragile gifts do not drop onto it.
		local cframe = hub.BeltSpawnCFrame * CFrame.new(0, size.Y / 2 - 2.3, 0)
		GiftService.Spawn(kind, cframe)
		task.wait(CONVEYOR_INTERVAL)
	end
end

local function onSleigh(player)
	if MissionService.GetPhase() ~= "Hub" or not MissionService.IsCrew(player) then
		return
	end
	local order = MissionService.GetOrder()
	if not order then
		return toast(player, "Сначала выберите заказ на доске.")
	end

	local carried = GiftService.CollectCarried(player)
	if #carried > 0 then
		for _, gift in ipairs(carried) do
			loaded += 1
			local slot = hub.SleighSlots[(loaded - 1) % #hub.SleighSlots + 1]
			GiftService.Park(gift, slot * CFrame.new(0, gift.Part.Size.Y / 2 - 1, 0))
		end
		MissionService.NotifyLoaded(loaded)
		refreshSleigh()
		return
	end

	if MissionService.CanDepart() then
		MissionService.Depart(player)
	else
		toast(player, string.format("Погружено %d из %d. Несите подарки с конвейера.", loaded, #order.Kinds))
	end
end

function HubService.Start(world)
	hub = world.Hub

	for index, card in ipairs(hub.Cards) do
		card.Prompt.Triggered:Connect(function(player)
			MissionService.SelectOrder(player, index)
		end)
	end

	hub.SleighPrompt.Triggered:Connect(onSleigh)

	hub.WorkbenchPrompt.Triggered:Connect(function(player)
		Remotes.OpenWorkbench:FireClient(player)
	end)

	hub.ExitPrompt.Triggered:Connect(function(player)
		MissionService.LeaveToLobby(player)
	end)

	MissionService.OrdersChanged:Connect(function(orders, selectedIndex)
		refreshBoard(orders, selectedIndex)
		refreshSleigh()
	end)

	MissionService.OrderSelected:Connect(function(order)
		loaded = 0
		refreshSleigh()
		runConveyor(order)
	end)

	MissionService.PhaseChanged:Connect(function(phase)
		if phase == "Hub" or phase == "Idle" then
			loaded = 0
			conveyorRun += 1
			if phase == "Idle" then
				refreshBoard({}, nil)
			end
			refreshSleigh()
		end
	end)

	refreshBoard(MissionService.GetOrders())
	refreshSleigh()
end

return HubService
