-- HUD built in code: noise meter, stamina, jetpack fuel, tips, objective, "in hands" plate,
-- sack and catnip counters, toasts and the results panel.
-- Reads replicated Player attributes only; the server owns every value.

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared:WaitForChild("Config"))
local Remotes = require(Shared:WaitForChild("Remotes"))

local HudController = {}

local COLOR_PANEL = Color3.fromRGB(28, 24, 34)
local COLOR_QUIET = Color3.fromRGB(110, 210, 130)
local COLOR_LOUD = Color3.fromRGB(240, 80, 70)
local COLOR_TEXT = Color3.fromRGB(250, 244, 230)

local function corner(instance, radius)
	local uiCorner = Instance.new("UICorner")
	uiCorner.CornerRadius = UDim.new(0, radius)
	uiCorner.Parent = instance
end

local function label(parent, text, size, position, textSize)
	local textLabel = Instance.new("TextLabel")
	textLabel.BackgroundTransparency = 1
	textLabel.Size = size
	textLabel.Position = position
	textLabel.Font = Enum.Font.GothamMedium
	textLabel.TextSize = textSize or 18
	textLabel.TextColor3 = COLOR_TEXT
	textLabel.TextStrokeTransparency = 0.5
	textLabel.TextWrapped = true
	textLabel.Text = text
	textLabel.Parent = parent
	return textLabel
end

local function bar(parent, title, position, fillColor)
	local holder = Instance.new("Frame")
	holder.AnchorPoint = Vector2.new(0.5, 1)
	holder.Position = position
	holder.Size = UDim2.fromOffset(320, 20)
	holder.BackgroundColor3 = COLOR_PANEL
	holder.BackgroundTransparency = 0.25
	holder.BorderSizePixel = 0
	holder.Parent = parent
	corner(holder, 8)

	local fill = Instance.new("Frame")
	fill.Size = UDim2.fromScale(0, 1)
	fill.BackgroundColor3 = fillColor
	fill.BorderSizePixel = 0
	fill.Parent = holder
	corner(fill, 8)

	local caption = label(holder, title, UDim2.fromScale(1, 1), UDim2.fromScale(0, 0), 13)
	caption.ZIndex = 3

	return holder, fill
end

function HudController.Start()
	local player = Players.LocalPlayer

	local gui = Instance.new("ScreenGui")
	gui.Name = "SilentNightHud"
	gui.ResetOnSpawn = false

	-- Objective (top)
	local objective = label(gui, "", UDim2.new(0.7, 0, 0, 56), UDim2.new(0.15, 0, 0, 8), 20)

	-- Tips (top right)
	local tips = label(gui, "", UDim2.fromOffset(220, 28), UDim2.new(1, -232, 0, 8), 18)
	tips.TextXAlignment = Enum.TextXAlignment.Right

	-- Bars (bottom centre)
	local fuelHolder, fuelFill = bar(gui, "РАНЕЦ [Q]", UDim2.new(0.5, 0, 1, -70), Color3.fromRGB(250, 160, 70))
	local noiseHolder, noiseFill = bar(gui, "ШУМ", UDim2.new(0.5, 0, 1, -44), COLOR_QUIET)
	local _, staminaFill = bar(gui, "СИЛЫ", UDim2.new(0.5, 0, 1, -18), Color3.fromRGB(110, 170, 240))

	-- Marker at the loudness that wakes Grandpa outright.
	local marker = Instance.new("Frame")
	marker.Size = UDim2.new(0, 2, 1, 6)
	marker.Position = UDim2.new(Config.Noise.WakeThreshold / 100, 0, 0, -3)
	marker.BackgroundColor3 = COLOR_TEXT
	marker.BorderSizePixel = 0
	marker.ZIndex = 2
	marker.Parent = noiseHolder

	-- "In hands" plate and counters (bottom left)
	local plate = Instance.new("Frame")
	plate.Position = UDim2.new(0, 12, 1, -132)
	plate.Size = UDim2.fromOffset(330, 92)
	plate.BackgroundColor3 = COLOR_PANEL
	plate.BackgroundTransparency = 0.3
	plate.BorderSizePixel = 0
	plate.Parent = gui
	corner(plate, 10)

	local holdingTitle = label(plate, "", UDim2.new(1, -16, 0, 24), UDim2.fromOffset(8, 6), 17)
	holdingTitle.TextXAlignment = Enum.TextXAlignment.Left
	local holdingHint = label(plate, "", UDim2.new(1, -16, 0, 34), UDim2.fromOffset(8, 28), 13)
	holdingHint.TextXAlignment = Enum.TextXAlignment.Left
	holdingHint.TextYAlignment = Enum.TextYAlignment.Top
	local counters = label(plate, "", UDim2.new(1, -16, 0, 20), UDim2.new(0, 8, 1, -26), 14)
	counters.TextXAlignment = Enum.TextXAlignment.Left

	local hint = label(
		gui,
		"Shift — бег   C — красться   E — взять/положить   R — мешок   F — мята   Q — ранец",
		UDim2.fromOffset(620, 22),
		UDim2.new(0, 12, 1, -30),
		13
	)
	hint.TextXAlignment = Enum.TextXAlignment.Left
	hint.TextTransparency = 0.25

	-- Status line above the bars ("hidden", "down")
	local status = label(gui, "", UDim2.new(0.4, 0, 0, 26), UDim2.new(0.3, 0, 1, -124), 18)

	-- Toast (lower centre)
	local toast = label(gui, "", UDim2.new(0.6, 0, 0, 40), UDim2.new(0.2, 0, 1, -170), 22)
	toast.Visible = false
	local toastVersion = 0

	-- Results panel (centre)
	local results = Instance.new("Frame")
	results.AnchorPoint = Vector2.new(0.5, 0.5)
	results.Position = UDim2.fromScale(0.5, 0.45)
	results.Size = UDim2.fromOffset(480, 340)
	results.BackgroundColor3 = COLOR_PANEL
	results.BackgroundTransparency = 0.1
	results.BorderSizePixel = 0
	results.Visible = false
	results.Parent = gui
	corner(results, 16)

	local resultsTitle = label(results, "", UDim2.new(1, -24, 0, 44), UDim2.fromOffset(12, 12), 30)
	resultsTitle.Font = Enum.Font.FredokaOne
	local resultsRank = label(results, "", UDim2.new(1, -24, 0, 30), UDim2.fromOffset(12, 58), 22)
	local resultsLines = label(results, "", UDim2.new(1, -48, 0, 170), UDim2.fromOffset(24, 96), 17)
	resultsLines.TextXAlignment = Enum.TextXAlignment.Left
	resultsLines.TextYAlignment = Enum.TextYAlignment.Top
	local resultsTips = label(results, "", UDim2.new(1, -24, 0, 40), UDim2.new(0, 12, 1, -56), 24)
	local resultsVersion = 0

	gui.Parent = player:WaitForChild("PlayerGui")

	local function attribute(name, default)
		local value = player:GetAttribute(name)
		if value == nil then
			return default
		end
		return value
	end

	local function refresh()
		local noise = attribute("Noise", Config.Noise.Idle)
		noiseFill.Size = UDim2.fromScale(math.clamp(noise / 100, 0, 1), 1)
		noiseFill.BackgroundColor3 = COLOR_QUIET:Lerp(COLOR_LOUD, math.clamp(noise / Config.Noise.Run, 0, 1))

		staminaFill.Size = UDim2.fromScale(math.clamp(attribute("Stamina", Config.Elf.MaxStamina) / Config.Elf.MaxStamina, 0, 1), 1)

		fuelHolder.Visible = attribute("Upg_Jetpack", 0) > 0
		fuelFill.Size = UDim2.fromScale(math.clamp(attribute("Fuel", Config.Jetpack.MaxFuel) / Config.Jetpack.MaxFuel, 0, 1), 1)

		tips.Text = string.format("Чаевые: %d", attribute("Tips", 0))
		objective.Text = attribute("Objective", "")

		local kind = attribute("Holding", "")
		local def = Config.GiftKinds[kind]
		holdingTitle.Text = def and ("В руках: " .. def.Name) or "В руках: пусто"
		holdingHint.Text = def and def.Hint or ""
		counters.Text = string.format(
			"Мешок [R]: %d/%d     Мята [F]: %d",
			attribute("SackCount", 0),
			attribute("SackCap", 1),
			attribute("Catnip", 0)
		)

		if attribute("Down", false) then
			status.Text = "Эльф упал…"
		elseif attribute("Hidden", false) then
			status.Text = "Спрятались. Дед не видит."
		else
			status.Text = ""
		end
	end

	player.AttributeChanged:Connect(refresh)
	refresh()

	Remotes.Toast.OnClientEvent:Connect(function(text)
		if typeof(text) ~= "string" then
			return
		end
		toastVersion += 1
		local version = toastVersion
		toast.Text = text
		toast.Visible = true
		task.delay(2.8, function()
			if toastVersion == version then
				toast.Visible = false
			end
		end)
	end)

	Remotes.RoundResult.OnClientEvent:Connect(function(result)
		if typeof(result) ~= "table" then
			return
		end
		resultsVersion += 1
		local version = resultsVersion
		resultsTitle.Text = result.Title or ""
		resultsRank.Text = "Звание: «" .. (result.Rank or "") .. "»"
		resultsLines.Text = table.concat(result.Lines or {}, "\n")
		resultsTips.Text = string.format("Чаевые: +%d", result.Tips or 0)
		results.Visible = true
		task.delay(result.Duration or 8, function()
			if resultsVersion == version then
				results.Visible = false
			end
		end)
	end)
end

return HudController
