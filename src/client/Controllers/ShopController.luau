-- Workbench / wardrobe panel: upgrades and catnip (tips), skins (tips or game pass),
-- and the Robux section (game passes, tip packs). Opened by the server (OpenWorkbench).
-- Every purchase is validated on the server; this file only draws buttons and sends requests.

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared:WaitForChild("Config"))
local Remotes = require(Shared:WaitForChild("Remotes"))

local ShopController = {}

local COLOR_PANEL = Color3.fromRGB(34, 28, 42)
local COLOR_ROW = Color3.fromRGB(52, 44, 62)
local COLOR_BUTTON = Color3.fromRGB(90, 170, 110)
local COLOR_BUTTON_OFF = Color3.fromRGB(90, 90, 100)
local COLOR_ROBUX = Color3.fromRGB(70, 150, 230)
local COLOR_TEXT = Color3.fromRGB(250, 244, 230)

local function corner(instance, radius)
	local uiCorner = Instance.new("UICorner")
	uiCorner.CornerRadius = UDim.new(0, radius)
	uiCorner.Parent = instance
end

local function text(parent, class, size, position, textSize)
	local instance = Instance.new(class)
	instance.Size = size
	instance.Position = position
	instance.Font = Enum.Font.GothamMedium
	instance.TextSize = textSize or 16
	instance.TextColor3 = COLOR_TEXT
	instance.TextWrapped = true
	instance.BorderSizePixel = 0
	instance.Parent = parent
	return instance
end

function ShopController.Start()
	local player = Players.LocalPlayer

	local gui = Instance.new("ScreenGui")
	gui.Name = "SilentNightShop"
	gui.ResetOnSpawn = false
	gui.DisplayOrder = 5
	gui.Enabled = false

	local panel = Instance.new("Frame")
	panel.AnchorPoint = Vector2.new(0.5, 0.5)
	panel.Position = UDim2.fromScale(0.5, 0.5)
	panel.Size = UDim2.new(0, 560, 0.8, 0)
	panel.BackgroundColor3 = COLOR_PANEL
	panel.BorderSizePixel = 0
	panel.Parent = gui
	corner(panel, 16)

	local title = text(panel, "TextLabel", UDim2.new(1, -120, 0, 40), UDim2.fromOffset(16, 10), 24)
	title.BackgroundTransparency = 1
	title.Font = Enum.Font.FredokaOne
	title.TextXAlignment = Enum.TextXAlignment.Left
	title.Text = "Верстак и шкаф эльфа"

	local tips = text(panel, "TextLabel", UDim2.fromOffset(200, 24), UDim2.new(0, 16, 0, 48), 16)
	tips.BackgroundTransparency = 1
	tips.TextXAlignment = Enum.TextXAlignment.Left

	local close = text(panel, "TextButton", UDim2.fromOffset(90, 34), UDim2.new(1, -106, 0, 14), 16)
	close.BackgroundColor3 = Color3.fromRGB(190, 70, 70)
	close.Text = "Закрыть"
	corner(close, 8)
	close.Activated:Connect(function()
		gui.Enabled = false
	end)

	local list = Instance.new("ScrollingFrame")
	list.Position = UDim2.fromOffset(12, 82)
	list.Size = UDim2.new(1, -24, 1, -94)
	list.BackgroundTransparency = 1
	list.BorderSizePixel = 0
	list.ScrollBarThickness = 8
	list.AutomaticCanvasSize = Enum.AutomaticSize.Y
	list.CanvasSize = UDim2.new(0, 0, 0, 0)
	list.Parent = panel

	local layout = Instance.new("UIListLayout")
	layout.Padding = UDim.new(0, 6)
	layout.SortOrder = Enum.SortOrder.LayoutOrder
	layout.Parent = list

	local order = 0
	local refreshers = {}

	local function header(caption)
		order += 1
		local heading = text(list, "TextLabel", UDim2.new(1, -12, 0, 30), UDim2.fromOffset(0, 0), 18)
		heading.BackgroundTransparency = 1
		heading.Font = Enum.Font.GothamBold
		heading.TextXAlignment = Enum.TextXAlignment.Left
		heading.Text = caption
		heading.LayoutOrder = order
	end

	-- A row with a caption on the left and one button on the right.
	-- `update()` returns (caption, buttonText, enabled); `onClick` sends the request.
	local function row(update, onClick, buttonColor)
		order += 1
		local frame = Instance.new("Frame")
		frame.Size = UDim2.new(1, -12, 0, 50)
		frame.BackgroundColor3 = COLOR_ROW
		frame.BorderSizePixel = 0
		frame.LayoutOrder = order
		frame.Parent = list
		corner(frame, 8)

		local caption = text(frame, "TextLabel", UDim2.new(1, -190, 1, 0), UDim2.fromOffset(12, 0), 15)
		caption.BackgroundTransparency = 1
		caption.TextXAlignment = Enum.TextXAlignment.Left

		local button = text(frame, "TextButton", UDim2.fromOffset(160, 36), UDim2.new(1, -170, 0.5, -18), 15)
		corner(button, 8)

		local enabled = false
		button.Activated:Connect(function()
			if enabled then
				onClick()
			end
		end)

		table.insert(refreshers, function()
			local captionText, buttonText, isEnabled = update()
			caption.Text = captionText
			button.Text = buttonText
			enabled = isEnabled
			button.BackgroundColor3 = isEnabled and (buttonColor or COLOR_BUTTON) or COLOR_BUTTON_OFF
		end)
	end

	local function attribute(name, default)
		local value = player:GetAttribute(name)
		if value == nil then
			return default
		end
		return value
	end

	local function ownsSkin(skin)
		if skin.GamePass then
			return attribute("Pass_" .. skin.GamePass, false) == true
		end
		local owned = string.split(attribute("OwnedSkins", "Classic"), ",")
		return table.find(owned, skin.Id) ~= nil
	end

	-- Upgrades
	header("Прокачка (за чаевые)")
	for _, key in ipairs(Config.UpgradeOrder) do
		local def = Config.Upgrades[key]
		row(function()
			local level = attribute("Upg_" .. key, 0)
			local caption = string.format("%s — уровень %d/%d", def.Name, level, Config.MaxUpgradeLevel)
			if level >= Config.MaxUpgradeLevel then
				return caption, "Максимум", false
			end
			local price = def.Base + level * def.Step
			return caption, string.format("Купить за %d", price), attribute("Tips", 0) >= price
		end, function()
			Remotes.BuyUpgrade:FireServer(key)
		end)
	end
	row(function()
		local count = attribute("Catnip", 0)
		local caption = string.format("Кошачья мята [F] — %d/%d", count, Config.Catnip.Max)
		if count >= Config.Catnip.Max then
			return caption, "Полный запас", false
		end
		return caption, string.format("Купить за %d", Config.Catnip.Price), attribute("Tips", 0) >= Config.Catnip.Price
	end, function()
		Remotes.BuyUpgrade:FireServer("Catnip")
	end)

	-- Skins
	header("Скины эльфа")
	for _, skin in ipairs(Config.Skins) do
		local pass = skin.GamePass and Config.GamePasses[skin.GamePass]
		row(function()
			if ownsSkin(skin) then
				if attribute("Skin", "Classic") == skin.Id then
					return skin.Name, "Надет", false
				end
				return skin.Name, "Надеть", true
			end
			if pass then
				return skin.Name .. " (геймпасс)", pass.Id ~= 0 and "Купить за Robux" or "Скоро", pass.Id ~= 0
			end
			return skin.Name, string.format("Купить за %d", skin.Price), attribute("Tips", 0) >= skin.Price
		end, function()
			if ownsSkin(skin) then
				Remotes.EquipSkin:FireServer(skin.Id)
			elseif pass then
				Remotes.PromptPurchase:FireServer("GamePass", skin.GamePass)
			else
				Remotes.BuySkin:FireServer(skin.Id)
			end
		end, pass and COLOR_ROBUX or nil)
	end

	-- Robux
	header("Поддержать игру (Robux)")
	for _, key in ipairs(Config.GamePassOrder) do
		local pass = Config.GamePasses[key]
		row(function()
			local caption = pass.Name .. " — " .. pass.Description
			if attribute("Pass_" .. key, false) == true then
				return caption, "Куплено", false
			end
			return caption, pass.Id ~= 0 and "Купить" or "Скоро", pass.Id ~= 0
		end, function()
			Remotes.PromptPurchase:FireServer("GamePass", key)
		end, COLOR_ROBUX)
	end
	for _, key in ipairs(Config.ProductOrder) do
		local product = Config.Products[key]
		row(function()
			local caption = string.format("%s — +%d чаевых", product.Name, product.Tips)
			return caption, product.Id ~= 0 and "Купить" or "Скоро", product.Id ~= 0
		end, function()
			Remotes.PromptPurchase:FireServer("Product", key)
		end, COLOR_ROBUX)
	end

	gui.Parent = player:WaitForChild("PlayerGui")

	local function refresh()
		tips.Text = string.format("Чаевые: %d", attribute("Tips", 0))
		for _, refresher in ipairs(refreshers) do
			refresher()
		end
	end

	player.AttributeChanged:Connect(function()
		if gui.Enabled then
			refresh()
		end
	end)

	Remotes.OpenWorkbench.OnClientEvent:Connect(function()
		refresh()
		gui.Enabled = true
	end)
end

return ShopController
