-- Robux purchases: developer products (tip packs) and game passes (VIP, golden skin).
-- Ids come from Config; an id of 0 means the item has not been created yet and is ignored.

local MarketplaceService = game:GetService("MarketplaceService")
local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared.Config)
local Remotes = require(Shared.Remotes)

local ProfileService = require(script.Parent.ProfileService)

local MonetizationService = {}

local productsById = {}
local passKeysById = {}

local function checkPasses(player)
	for key, pass in pairs(Config.GamePasses) do
		local owned = false
		if pass.Id ~= 0 then
			local ok, result = pcall(function()
				return MarketplaceService:UserOwnsGamePassAsync(player.UserId, pass.Id)
			end)
			owned = ok and result == true
		end
		if player.Parent then
			player:SetAttribute("Pass_" .. key, owned)
		end
	end
	ProfileService.SkinChanged:Fire(player)
end

local function processReceipt(receipt)
	local player = Players:GetPlayerByUserId(receipt.PlayerId)
	local product = productsById[receipt.ProductId]
	if not product then
		-- Unknown product: nothing we can grant. Do not keep retrying forever.
		return Enum.ProductPurchaseDecision.PurchaseGranted
	end
	if not player or not ProfileService.Get(player) then
		-- Player left or the profile is not loaded yet: Roblox will call again later.
		return Enum.ProductPurchaseDecision.NotProcessedYet
	end

	-- A profile that could not be loaded is never saved, so a grant would be lost when the
	-- player leaves. Leave the receipt pending; Roblox retries on a later join.
	if not ProfileService.Get(player).CanSave then
		Remotes.Toast:FireClient(player, "Покупка будет зачислена, когда заработает сохранение.")
		return Enum.ProductPurchaseDecision.NotProcessedYet
	end

	if not ProfileService.RememberReceipt(player, receipt.PurchaseId) then
		ProfileService.AddTips(player, product.Tips)
		Remotes.Toast:FireClient(player, string.format("%s: +%d чаевых", product.Name, product.Tips))
	end

	-- Only confirm once the grant is safely stored; if the save fails Roblox retries and the
	-- remembered receipt id prevents a double grant.
	if not ProfileService.Save(player) then
		return Enum.ProductPurchaseDecision.NotProcessedYet
	end
	return Enum.ProductPurchaseDecision.PurchaseGranted
end

function MonetizationService.Start()
	for _, product in pairs(Config.Products) do
		if product.Id ~= 0 then
			productsById[product.Id] = product
		end
	end
	for key, pass in pairs(Config.GamePasses) do
		if pass.Id ~= 0 then
			passKeysById[pass.Id] = key
		end
	end

	MarketplaceService.ProcessReceipt = processReceipt

	MarketplaceService.PromptGamePassPurchaseFinished:Connect(function(player, passId, purchased)
		local key = passKeysById[passId]
		if purchased and key then
			player:SetAttribute("Pass_" .. key, true)
			ProfileService.SkinChanged:Fire(player)
			Remotes.Toast:FireClient(player, "Спасибо! Получено: " .. Config.GamePasses[key].Name)
		end
	end)

	Remotes.PromptPurchase.OnServerEvent:Connect(function(player, kind, key)
		if typeof(kind) ~= "string" or typeof(key) ~= "string" then
			return
		end
		if kind == "Product" then
			local product = Config.Products[key]
			if product and product.Id ~= 0 then
				MarketplaceService:PromptProductPurchase(player, product.Id)
			end
		elseif kind == "GamePass" then
			local pass = Config.GamePasses[key]
			if pass and pass.Id ~= 0 and not ProfileService.HasPass(player, key) then
				MarketplaceService:PromptGamePassPurchase(player, pass.Id)
			end
		end
	end)

	Players.PlayerAdded:Connect(checkPasses)
	for _, player in ipairs(Players:GetPlayers()) do
		task.spawn(checkPasses, player)
	end
end

return MonetizationService
