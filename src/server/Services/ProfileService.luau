-- Player progress: tips, upgrade levels, catnip, skins. Saved with DataStore.
-- Everything the client needs is mirrored into Player attributes:
--   Tips, Catnip, Upg_<Name>, Skin, OwnedSkins ("A,B,C"), Pass_<Name>
--
-- If the DataStore cannot be read (Studio without API access, outage) the player gets a temporary
-- default profile that is never written back, so a failed load can never wipe real progress.

local DataStoreService = game:GetService("DataStoreService")
local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared.Config)
local Remotes = require(Shared.Remotes)
local Signal = require(Shared.Signal)

local STORE_NAME = "SilentNightProfile_v1"
local MAX_RECEIPTS = 60

local ProfileService = {}

-- Fires with (player) after the equipped skin changed or the profile finished loading.
ProfileService.SkinChanged = Signal.new()

local store
local profiles = {}
local lastRequestAt = {}

local skinsById = {}
for _, skin in ipairs(Config.Skins) do
	skinsById[skin.Id] = skin
end

local function newProfile()
	local upgrades = {}
	for _, key in ipairs(Config.UpgradeOrder) do
		upgrades[key] = 0
	end
	return {
		Tips = Config.Economy.StartTips,
		Upgrades = upgrades,
		Catnip = 0,
		Skins = { "Classic" },
		Skin = "Classic",
		Receipts = {},
	}
end

-- Never trust stored data blindly: clamp and fill gaps.
local function sanitize(data)
	local profile = newProfile()
	if typeof(data) ~= "table" then
		return profile
	end
	if typeof(data.Tips) == "number" then
		profile.Tips = math.max(0, math.floor(data.Tips))
	end
	if typeof(data.Catnip) == "number" then
		profile.Catnip = math.clamp(math.floor(data.Catnip), 0, Config.Catnip.Max)
	end
	if typeof(data.Upgrades) == "table" then
		for _, key in ipairs(Config.UpgradeOrder) do
			local level = data.Upgrades[key]
			if typeof(level) == "number" then
				profile.Upgrades[key] = math.clamp(math.floor(level), 0, Config.MaxUpgradeLevel)
			end
		end
	end
	if typeof(data.Skins) == "table" then
		for _, id in ipairs(data.Skins) do
			if skinsById[id] and not table.find(profile.Skins, id) then
				table.insert(profile.Skins, id)
			end
		end
	end
	if typeof(data.Skin) == "string" and skinsById[data.Skin] then
		profile.Skin = data.Skin
	end
	if typeof(data.Receipts) == "table" then
		for _, id in ipairs(data.Receipts) do
			if typeof(id) == "string" then
				table.insert(profile.Receipts, id)
			end
		end
	end
	return profile
end

local function publish(player, profile)
	player:SetAttribute("Tips", profile.Tips)
	player:SetAttribute("Catnip", profile.Catnip)
	for _, key in ipairs(Config.UpgradeOrder) do
		player:SetAttribute("Upg_" .. key, profile.Upgrades[key])
	end
	player:SetAttribute("Skin", profile.Skin)
	player:SetAttribute("OwnedSkins", table.concat(profile.Skins, ","))
end

local function toast(player, text)
	Remotes.Toast:FireClient(player, text)
end

local function throttled(player)
	local now = os.clock()
	if lastRequestAt[player] and now - lastRequestAt[player] < 0.2 then
		return true
	end
	lastRequestAt[player] = now
	return false
end

function ProfileService.Get(player)
	return profiles[player]
end

function ProfileService.GetLevel(player, upgrade)
	local profile = profiles[player]
	return profile and profile.Upgrades[upgrade] or 0
end

function ProfileService.HasPass(player, passKey)
	return player:GetAttribute("Pass_" .. passKey) == true
end

function ProfileService.AddTips(player, amount)
	local profile = profiles[player]
	if not profile then
		return
	end
	profile.Tips = math.max(0, profile.Tips + math.floor(amount))
	publish(player, profile)
end

function ProfileService.UseCatnip(player)
	local profile = profiles[player]
	if not profile or profile.Catnip <= 0 then
		return false
	end
	profile.Catnip -= 1
	publish(player, profile)
	return true
end

-- Returns true if this receipt was already granted (so it must not be granted twice).
function ProfileService.RememberReceipt(player, purchaseId)
	local profile = profiles[player]
	if not profile then
		return false
	end
	if table.find(profile.Receipts, purchaseId) then
		return true
	end
	table.insert(profile.Receipts, purchaseId)
	while #profile.Receipts > MAX_RECEIPTS do
		table.remove(profile.Receipts, 1)
	end
	return false
end

function ProfileService.OwnsSkin(player, skinId)
	local skin = skinsById[skinId]
	local profile = profiles[player]
	if not skin or not profile then
		return false
	end
	if skin.GamePass then
		return ProfileService.HasPass(player, skin.GamePass)
	end
	return table.find(profile.Skins, skinId) ~= nil
end

function ProfileService.GetSkin(player)
	local profile = profiles[player]
	local id = profile and profile.Skin or "Classic"
	if not ProfileService.OwnsSkin(player, id) then
		id = "Classic"
	end
	return skinsById[id]
end

-- Returns true only if the data was really written (or there is nothing that should be written).
function ProfileService.Save(player)
	local profile = profiles[player]
	if not profile or not profile.CanSave or not store then
		return false
	end
	local data = {
		Tips = profile.Tips,
		Upgrades = profile.Upgrades,
		Catnip = profile.Catnip,
		Skins = profile.Skins,
		Skin = profile.Skin,
		Receipts = profile.Receipts,
	}
	local ok, message = pcall(function()
		store:SetAsync("u_" .. player.UserId, data)
	end)
	if not ok then
		warn("[Profile] save failed for " .. player.Name .. ": " .. tostring(message))
	end
	return ok
end

local function buyUpgrade(player, profile, key)
	if key == "Catnip" then
		if profile.Catnip >= Config.Catnip.Max then
			return toast(player, "Больше мяты не унести.")
		end
		if profile.Tips < Config.Catnip.Price then
			return toast(player, "Не хватает чаевых.")
		end
		profile.Tips -= Config.Catnip.Price
		profile.Catnip += 1
		publish(player, profile)
		return toast(player, "Куплен пакетик кошачьей мяты.")
	end

	local def = Config.Upgrades[key]
	if not def then
		return
	end
	local level = profile.Upgrades[key]
	if level >= Config.MaxUpgradeLevel then
		return toast(player, "Уже максимальный уровень.")
	end
	local price = def.Base + level * def.Step
	if profile.Tips < price then
		return toast(player, "Не хватает чаевых.")
	end
	profile.Tips -= price
	profile.Upgrades[key] = level + 1
	publish(player, profile)
	toast(player, string.format("%s: уровень %d", def.Name, level + 1))
end

local function buySkin(player, profile, skinId)
	local skin = skinsById[skinId]
	if not skin or skin.GamePass or ProfileService.OwnsSkin(player, skinId) then
		return
	end
	if profile.Tips < skin.Price then
		return toast(player, "Не хватает чаевых.")
	end
	profile.Tips -= skin.Price
	table.insert(profile.Skins, skinId)
	profile.Skin = skinId
	publish(player, profile)
	ProfileService.SkinChanged:Fire(player)
	toast(player, "Новый скин: " .. skin.Name)
end

local function equipSkin(player, profile, skinId)
	if not ProfileService.OwnsSkin(player, skinId) then
		return
	end
	profile.Skin = skinId
	publish(player, profile)
	ProfileService.SkinChanged:Fire(player)
end

local function load(player)
	local profile
	local loaded = false
	if store then
		local ok, data = pcall(function()
			return store:GetAsync("u_" .. player.UserId)
		end)
		if ok then
			profile = sanitize(data)
			loaded = true
		else
			warn("[Profile] load failed for " .. player.Name .. ": " .. tostring(data))
		end
	end
	profile = profile or newProfile()
	profile.CanSave = loaded

	if not player.Parent then
		return
	end
	profiles[player] = profile
	publish(player, profile)
	ProfileService.SkinChanged:Fire(player)
	if not loaded then
		toast(player, "Сохранение недоступно: прогресс этой сессии не запишется.")
	end
end

local function onRequest(handler)
	return function(player, argument)
		local profile = profiles[player]
		if not profile or typeof(argument) ~= "string" or throttled(player) then
			return
		end
		handler(player, profile, argument)
	end
end

function ProfileService.Start()
	local ok, result = pcall(function()
		return DataStoreService:GetDataStore(STORE_NAME)
	end)
	if ok then
		store = result
	else
		warn("[Profile] DataStore unavailable: " .. tostring(result))
	end

	Remotes.BuyUpgrade.OnServerEvent:Connect(onRequest(buyUpgrade))
	Remotes.BuySkin.OnServerEvent:Connect(onRequest(buySkin))
	Remotes.EquipSkin.OnServerEvent:Connect(onRequest(equipSkin))

	Players.PlayerAdded:Connect(load)
	for _, player in ipairs(Players:GetPlayers()) do
		task.spawn(load, player)
	end

	Players.PlayerRemoving:Connect(function(player)
		ProfileService.Save(player)
		profiles[player] = nil
		lastRequestAt[player] = nil
	end)

	game:BindToClose(function()
		for _, player in ipairs(Players:GetPlayers()) do
			task.spawn(ProfileService.Save, player)
		end
		task.wait(2)
	end)
end

return ProfileService
