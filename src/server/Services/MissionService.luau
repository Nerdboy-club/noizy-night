-- The delivery loop for one crew:
--   Hub (pick an order, load the sleigh) -> Rooftop (find the chimney) -> House (deliver under the tree,
--   leave through the fireplace) -> Results -> Hub ...
--
-- Two ways a crew gets here:
--   * Published game: the lobby teleports the party to a reserved server; everyone who joins that
--     server is crew and the mission starts in the Hub right away.
--   * Studio (no reserved servers): the lobby calls Begin() and the mission runs in the same server.
--     Only one crew at a time in that mode.
--
-- Other services never talk to each other about mission flow; they raise signals and this module reacts.

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")
local TeleportService = game:GetService("TeleportService")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared.Config)
local Remotes = require(Shared.Remotes)
local Signal = require(Shared.Signal)

local NoiseService = require(script.Parent.NoiseService)
local ProfileService = require(script.Parent.ProfileService)
local ElfService = require(script.Parent.ElfService)
local GiftService = require(script.Parent.GiftService)
local GrandpaService = require(script.Parent.GrandpaService)
local CatService = require(script.Parent.CatService)
local OrderService = require(script.Parent.OrderService)

local MissionService = {}

-- Fires with (phase): "Idle" | "Hub" | "Rooftop" | "House" | "Results".
MissionService.PhaseChanged = Signal.new()
-- Fires with (orders, selectedIndex or nil).
MissionService.OrdersChanged = Signal.new()
-- Fires with (order) when the crew picked an order.
MissionService.OrderSelected = Signal.new()

-- True in a reserved server created by the lobby (never in Studio).
MissionService.IsMissionServer = game.PrivateServerId ~= "" and game.PrivateServerOwnerId == 0

local NOISE_NAMES = {
	Step = "топот",
	Creak = "скрип половицы",
	Land = "приземление",
	Bump = "удар о стену",
	Bonk = "столкновение лбами",
	Toy = "писк игрушки",
	Jetpack = "рёв ранца",
	Gift = "грохот подарка",
	Glass = "разбитый снежный шар",
	Tick = "тиканье игрушки",
	Alarm = "звон шкатулки",
	Buzz = "жужжание машинки",
	DoorCreak = "скрип двери",
	DoorSlam = "хлопок двери",
	Cat = "вопль кота",
	Meow = "мяуканье",
}

local IN_BED = { Asleep = true, Stirring = true, Suspicious = true }

local world
local exitPrompt
local phase = "Idle"
local crew = {}
local orders = {}
local order
local loadedCount = 0
local deliveredCount = 0
local stats

local function resetStats()
	stats = {
		loudest = 0,
		loudestKind = nil,
		wakeUps = 0,
		chases = 0,
		falls = 0,
		yowls = 0,
		startedAt = os.clock(),
	}
end

local function setObjective(text)
	for _, player in ipairs(crew) do
		player:SetAttribute("Objective", text)
	end
end

local function toastCrew(text)
	for _, player in ipairs(crew) do
		Remotes.Toast:FireClient(player, text)
	end
end

local function setPhase(newPhase)
	phase = newPhase
	for _, player in ipairs(crew) do
		player:SetAttribute("Phase", newPhase)
	end
	MissionService.PhaseChanged:Fire(newPhase)
end

local function phaseSpawn()
	if phase == "Rooftop" then
		return world.Rooftop.SpawnCFrame
	elseif phase == "House" then
		return world.House.SpawnCFrame
	end
	return world.Hub.SpawnCFrame
end

local function teleportCrew(cframe)
	local count = #crew
	for index, player in ipairs(crew) do
		local offset = (index - (count + 1) / 2) * 4
		task.spawn(ElfService.Teleport, player, cframe * CFrame.new(offset, 0, 0))
	end
end

local function quietHouse()
	GrandpaService.Reset()
	GrandpaService.SetActive(false)
	CatService.Reset()
	CatService.SetActive(false)
	exitPrompt.Enabled = false
end

local function enterHub()
	quietHouse()
	GiftService.Clear()
	GiftService.SetDeliveryZone(nil)
	GiftService.SetFallback(world.Hub.SpawnCFrame)
	order = nil
	loadedCount = 0
	deliveredCount = 0
	orders = OrderService.Generate(3)

	setPhase("Hub")
	MissionService.OrdersChanged:Fire(orders, nil)
	for _, player in ipairs(crew) do
		ElfService.Recover(player)
	end
	teleportCrew(world.Hub.SpawnCFrame)
	setObjective("Выберите заказ на доске [E].")
end

local function endMission()
	quietHouse()
	GiftService.Clear()
	GiftService.SetDeliveryZone(nil)
	order = nil
	orders = {}
	phase = "Idle"
	MissionService.PhaseChanged:Fire("Idle")
end

local function enterHouse()
	local house = world.House
	resetStats()
	house.SetMood(order.Spooky)

	GiftService.Clear()
	GiftService.SetFallback(house.SpawnCFrame)
	GiftService.SetDeliveryZone(house.TreePosition, house.DeliveryRadius)

	-- Shuffle the landing spots so every run plays a little differently.
	local spots = table.clone(house.GiftSpawns)
	for index = #spots, 2, -1 do
		local swap = math.random(1, index)
		spots[index], spots[swap] = spots[swap], spots[index]
	end
	local nextSpot = 1
	for _, kind in ipairs(order.Kinds) do
		if kind == "Huge" then
			GiftService.Spawn(kind, house.HugeGiftSpawn)
		else
			GiftService.Spawn(kind, spots[nextSpot])
			nextSpot = nextSpot % #spots + 1
		end
	end
	deliveredCount = 0

	GrandpaService.Reset()
	GrandpaService.SetSpooky(order.Spooky)
	GrandpaService.SetActive(true)
	CatService.Reset()
	CatService.SetActive(true)
	exitPrompt.Enabled = false

	setPhase("House")
	teleportCrew(house.SpawnCFrame)
	setObjective(string.format("Подарки разлетелись по дому. Сложите их под ёлку (0/%d).", #order.Kinds))
	if order.Spooky then
		toastCrew("Жуткий дом… Дед здесь спит очень чутко.")
	end
end

local function buildResult(success)
	local total = 0
	for _, gift in ipairs(GiftService.GetAll()) do
		if gift.State == "Delivered" then
			total += gift.Integrity
		end
	end
	local average = #order.Kinds > 0 and total / #order.Kinds or 0
	local tips = success and math.floor(order.BaseReward * average / 100 * order.TipMultiplier + 0.5) or 0

	local rank = "Тише мыши"
	if not success or stats.chases > 0 then
		rank = "Слон в посудной лавке"
	elseif stats.wakeUps > 0 or stats.yowls > 0 then
		rank = "Почти ниндзя"
	end

	local lines = {}
	if stats.loudestKind then
		local name = NOISE_NAMES[stats.loudestKind] or "шум"
		table.insert(lines, string.format("Самый громкий звук: %s (%d)", name, stats.loudest))
	end
	if stats.wakeUps > 0 then
		local suffix = stats.chases > 0 and "… и даже гонялся с тапком" or ""
		table.insert(lines, string.format("Дед просыпался: %d%s", stats.wakeUps, suffix))
	else
		table.insert(lines, "Дед даже не проснулся")
	end
	if stats.yowls > 0 then
		table.insert(lines, string.format("Кот орал: %d", stats.yowls))
	end
	table.insert(lines, string.format("Эльфы падали: %d", stats.falls))
	table.insert(lines, string.format("Упаковка в среднем: %d%%", math.floor(average + 0.5)))
	table.insert(lines, string.format("Время в доме: %d с", math.floor(os.clock() - stats.startedAt)))

	return {
		Success = success,
		Title = success and "Доставлено!" or "Пойманы!",
		Rank = rank,
		Lines = lines,
		Tips = tips,
		Duration = Config.Round.ResultsTime,
	}
end

local function finish(success)
	if phase ~= "House" then
		return
	end
	local result = buildResult(success)
	setPhase("Results")
	GrandpaService.SetActive(false)
	CatService.SetActive(false)
	exitPrompt.Enabled = false
	setObjective("")

	for _, player in ipairs(crew) do
		local payout = result.Tips
		if ProfileService.HasPass(player, "Vip") then
			payout = math.floor(payout * Config.VipTipMultiplier + 0.5)
		end
		ProfileService.AddTips(player, payout)
		task.spawn(ProfileService.Save, player)

		local personal = table.clone(result)
		personal.Tips = payout
		Remotes.RoundResult:FireClient(player, personal)
	end

	task.delay(Config.Round.ResultsTime, function()
		if phase == "Results" and #crew > 0 then
			enterHub()
		end
	end)
end

local function onCaught(player)
	if phase ~= "House" or not MissionService.IsCrew(player) then
		return
	end
	-- Grandpa grabs the elf by the collar and throws it back into the fireplace.
	local coop = #crew > 1
	ElfService.Knockdown(player, Vector3.new(0, 10, 12), "Caught", coop and Config.Elf.CaughtDownTimeCoop or nil)
	task.spawn(ElfService.Teleport, player, world.House.SpawnCFrame)

	local anyoneStanding = false
	for _, member in ipairs(crew) do
		if not ElfService.IsDown(member) then
			anyoneStanding = true
			break
		end
	end
	if anyoneStanding then
		toastCrew(player.DisplayName .. " пойман! Дед выкинул его в камин. Поднимите друга [E].")
	else
		finish(false)
	end
end

local function removeCrew(player)
	local index = table.find(crew, player)
	if not index then
		return
	end
	table.remove(crew, index)
	if player.Parent then
		player:SetAttribute("Phase", "Lobby")
		player:SetAttribute("Objective", Config.Lobby.Objective)
	end
	if #crew == 0 then
		endMission()
	end
end

function MissionService.GetPhase()
	return phase
end

function MissionService.GetCrew()
	return crew
end

function MissionService.IsCrew(player)
	return table.find(crew, player) ~= nil
end

function MissionService.GetOrder()
	return order
end

-- Returns (orders, selectedIndex or nil).
function MissionService.GetOrders()
	return orders, order and table.find(orders, order) or nil
end

-- Where a (re)spawning player belongs right now. Returns nil until the world is ready.
function MissionService.GetSpawnCFrame(player)
	if not world then
		return nil
	end
	if MissionService.IsCrew(player) then
		return phaseSpawn()
	end
	if MissionService.IsMissionServer then
		return world.Hub.SpawnCFrame
	end
	return world.Lobby.SpawnCFrame
end

-- Starts a mission for a party (Studio / same-server mode). Returns false if one is already running.
function MissionService.Begin(players)
	if phase ~= "Idle" then
		return false
	end
	crew = {}
	for _, player in ipairs(players) do
		if player.Parent then
			table.insert(crew, player)
		end
	end
	if #crew == 0 then
		return false
	end
	enterHub()
	return true
end

-- Adds a late joiner to the running mission (reserved-server mode).
function MissionService.AddCrew(player)
	if MissionService.IsCrew(player) then
		return
	end
	table.insert(crew, player)
	if phase == "Idle" then
		enterHub()
		return
	end
	player:SetAttribute("Phase", phase)
	player:SetAttribute("Objective", "Смена уже идёт. Догоняйте команду!")
	task.spawn(ElfService.Teleport, player, phaseSpawn())
end

function MissionService.SelectOrder(player, index)
	if phase ~= "Hub" or order or not MissionService.IsCrew(player) then
		return
	end
	local chosen = orders[index]
	if not chosen then
		return
	end
	order = chosen
	loadedCount = 0
	MissionService.OrdersChanged:Fire(orders, index)
	MissionService.OrderSelected:Fire(order)
	setObjective(string.format("Несите подарки с конвейера в сани (0/%d).", #order.Kinds))
	toastCrew(string.format("Заказ принят: труба №%d.", order.Address))
end

-- Called by HubService whenever gifts were loaded onto the sleigh.
function MissionService.NotifyLoaded(count)
	if phase ~= "Hub" or not order then
		return
	end
	loadedCount = count
	if loadedCount >= #order.Kinds then
		setObjective("Всё погружено! Отправляйтесь на санях [E].")
	else
		setObjective(string.format("Несите подарки с конвейера в сани (%d/%d).", loadedCount, #order.Kinds))
	end
end

function MissionService.CanDepart()
	return phase == "Hub" and order ~= nil and loadedCount >= #order.Kinds
end

function MissionService.Depart(player)
	if not MissionService.CanDepart() or not MissionService.IsCrew(player) then
		return
	end
	GiftService.Clear()
	GiftService.SetFallback(world.Rooftop.SpawnCFrame)
	setPhase("Rooftop")
	teleportCrew(world.Rooftop.SpawnCFrame)
	setObjective(string.format("Найдите трубу №%d и нырните в дымоход [E].", order.Address))
end

function MissionService.TryChimney(player, number)
	if phase ~= "Rooftop" or not order or not MissionService.IsCrew(player) then
		return
	end
	if number ~= order.Address then
		Remotes.Toast:FireClient(player, string.format("Не тот адрес! Нужна труба №%d.", order.Address))
		return
	end
	enterHouse()
end

function MissionService.LeaveToLobby(player)
	if not MissionService.IsCrew(player) then
		return
	end
	if MissionService.IsMissionServer and not RunService:IsStudio() then
		local ok = pcall(function()
			TeleportService:Teleport(game.PlaceId, player)
		end)
		if not ok then
			Remotes.Toast:FireClient(player, "Не получилось вернуться в лобби. Попробуйте ещё раз.")
		end
		return
	end
	removeCrew(player)
	task.spawn(ElfService.Teleport, player, world.Lobby.SpawnCFrame)
end

function MissionService.Start(worldRefs)
	world = worldRefs
	resetStats()

	exitPrompt = Instance.new("ProximityPrompt")
	exitPrompt.ObjectText = "Камин"
	exitPrompt.ActionText = "Уйти через дымоход"
	exitPrompt.KeyboardKeyCode = Enum.KeyCode.E
	exitPrompt.MaxActivationDistance = 10
	exitPrompt.RequiresLineOfSight = false
	exitPrompt.Enabled = false
	exitPrompt.Parent = world.House.Fireplace

	exitPrompt.Triggered:Connect(function(player)
		if phase == "House" and MissionService.IsCrew(player) and deliveredCount >= #order.Kinds then
			finish(true)
		end
	end)

	NoiseService.NoiseEmitted:Connect(function(event)
		if phase == "House" and event.Loudness > stats.loudest then
			stats.loudest = math.floor(event.Loudness + 0.5)
			stats.loudestKind = event.Kind
		end
	end)

	GrandpaService.StateChanged:Connect(function(newState, oldState)
		if phase ~= "House" then
			return
		end
		if newState == "Searching" and IN_BED[oldState] then
			stats.wakeUps += 1
			toastCrew("Дед проснулся и идёт проверять!")
		elseif newState == "Chasing" then
			stats.chases += 1
		end
	end)

	GrandpaService.Caught:Connect(onCaught)

	CatService.Yowled:Connect(function()
		if phase == "House" then
			stats.yowls += 1
		end
	end)

	ElfService.Fell:Connect(function(player, reason)
		if phase == "House" and MissionService.IsCrew(player) then
			stats.falls += 1
		end
		if reason == "Bump" then
			Remotes.Toast:FireClient(player, "Бум! Эльф врезался в стену.")
		elseif reason == "Land" then
			Remotes.Toast:FireClient(player, "Плюх! Жёсткое приземление.")
		elseif reason == "Toy" then
			Remotes.Toast:FireClient(player, "Пи-и! Эльф наступил на игрушку.")
		elseif reason == "Bonk" then
			Remotes.Toast:FireClient(player, "Бдыщ! Эльфы столкнулись лбами.")
		end
	end)

	ElfService.HelpedUp:Connect(function(player, helper)
		Remotes.Toast:FireClient(player, helper.DisplayName .. " поднял вас!")
		Remotes.Toast:FireClient(helper, "Вы подняли " .. player.DisplayName .. "!")
	end)

	GiftService.Broken:Connect(function()
		if phase == "House" then
			toastCrew("Дзынь! Снежный шар разбился. Чаевых за него не будет.")
		end
	end)

	GiftService.Delivered:Connect(function()
		if phase ~= "House" then
			return
		end
		deliveredCount += 1
		if deliveredCount >= #order.Kinds then
			exitPrompt.Enabled = true
			setObjective("Все подарки под ёлкой! Уходите через камин [E].")
			toastCrew("Все подарки доставлены!")
		else
			setObjective(string.format("Сложите подарки под ёлку (%d/%d).", deliveredCount, #order.Kinds))
		end
	end)

	local function onPlayerAdded(player)
		if MissionService.IsMissionServer then
			MissionService.AddCrew(player)
		else
			player:SetAttribute("Phase", "Lobby")
			player:SetAttribute("Objective", Config.Lobby.Objective)
		end
	end
	Players.PlayerAdded:Connect(onPlayerAdded)
	for _, player in ipairs(Players:GetPlayers()) do
		onPlayerAdded(player)
	end

	Players.PlayerRemoving:Connect(removeCrew)
end

return MissionService
