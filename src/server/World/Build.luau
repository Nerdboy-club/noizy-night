-- Shared blockout helpers used by every world builder.

local Lighting = game:GetService("Lighting")

local Build = {}

function Build.block(parent, name, size, position, color, material)
	local part = Instance.new("Part")
	part.Name = name
	part.Anchored = true
	part.Size = size
	part.Position = position
	part.Color = color
	part.Material = material or Enum.Material.SmoothPlastic
	part.TopSurface = Enum.SurfaceType.Smooth
	part.BottomSurface = Enum.SurfaceType.Smooth
	part.Parent = parent
	return part
end

-- Decorative part that neither collides nor blocks raycasts.
function Build.ghost(parent, name, size, position, color, material)
	local part = Build.block(parent, name, size, position, color, material)
	part.CanCollide = false
	part.CanQuery = false
	part.CanTouch = false
	return part
end

-- Floors carry a "Surface" attribute (Carpet/Wood/Tile/Ice) that the noise system reads.
function Build.floor(parent, name, size, position, color, material, surface, creaky)
	local part = Build.block(parent, name, size, position, color, material)
	part:SetAttribute("Surface", surface)
	if creaky then
		part:SetAttribute("Creaky", true)
	end
	return part
end

-- Axis-aligned wall from (x1, z1) to (x2, z2).
-- options: BaseY, Height, Color, Material, Thickness, DoorHeight,
--          Doors = { {from, to}, ... } measured along the axis the wall runs on.
function Build.wall(parent, name, x1, z1, x2, z2, options)
	local thickness = options.Thickness or 1
	local baseY = options.BaseY
	local topY = baseY + options.Height
	local doorHeight = options.DoorHeight or 9
	local alongX = z1 == z2
	local from = alongX and math.min(x1, x2) or math.min(z1, z2)
	local to = alongX and math.max(x1, x2) or math.max(z1, z2)
	local fixed = alongX and z1 or x1

	local function piece(a, b, y0, y1)
		if b - a <= 0.01 or y1 - y0 <= 0.01 then
			return
		end
		local mid = (a + b) / 2
		local size, position
		if alongX then
			size = Vector3.new(b - a, y1 - y0, thickness)
			position = Vector3.new(mid, (y0 + y1) / 2, fixed)
		else
			size = Vector3.new(thickness, y1 - y0, b - a)
			position = Vector3.new(fixed, (y0 + y1) / 2, mid)
		end
		Build.block(parent, name, size, position, options.Color, options.Material)
	end

	local doors = table.clone(options.Doors or {})
	table.sort(doors, function(a, b)
		return a[1] < b[1]
	end)

	-- Extend the ends a little so corners close up.
	local cursor = from - thickness / 2
	for _, door in ipairs(doors) do
		piece(cursor, door[1], baseY, topY)
		piece(door[1], door[2], baseY + doorHeight, topY)
		cursor = door[2]
	end
	piece(cursor, to + thickness / 2, baseY, topY)
end

function Build.pointLight(part, color, brightness, range)
	local light = Instance.new("PointLight")
	light.Color = color
	light.Brightness = brightness
	light.Range = range
	light.Shadows = true
	light.Parent = part
	return light
end

function Build.lamp(parent, name, position, color, brightness, range)
	local bulb = Build.ghost(parent, name, Vector3.new(1, 1, 1), position, color, Enum.Material.Neon)
	bulb.Shape = Enum.PartType.Ball
	return Build.pointLight(bulb, color, brightness, range), bulb
end

-- Text on a face of a part. Returns the TextLabel.
function Build.sign(part, face, text, textColor, backgroundColor)
	local gui = Instance.new("SurfaceGui")
	gui.Face = face
	gui.SizingMode = Enum.SurfaceGuiSizingMode.PixelsPerStud
	gui.PixelsPerStud = 30
	gui.Parent = part

	local label = Instance.new("TextLabel")
	label.Size = UDim2.fromScale(1, 1)
	label.BackgroundTransparency = backgroundColor and 0 or 1
	label.BackgroundColor3 = backgroundColor or Color3.new(0, 0, 0)
	label.Font = Enum.Font.GothamBold
	label.TextScaled = true
	label.TextWrapped = true
	label.TextColor3 = textColor or Color3.new(1, 1, 1)
	label.Text = text
	label.Parent = gui

	local padding = Instance.new("UIPadding")
	padding.PaddingLeft = UDim.new(0.05, 0)
	padding.PaddingRight = UDim.new(0.05, 0)
	padding.PaddingTop = UDim.new(0.05, 0)
	padding.PaddingBottom = UDim.new(0.05, 0)
	padding.Parent = label
	return label
end

-- Floating text above a part. Returns the TextLabel.
function Build.billboard(part, text, offsetY, width, height)
	local gui = Instance.new("BillboardGui")
	gui.Adornee = part
	gui.Size = UDim2.fromOffset(width or 200, height or 60)
	gui.StudsOffset = Vector3.new(0, offsetY or 4, 0)
	gui.AlwaysOnTop = false
	gui.MaxDistance = 160
	gui.Parent = part

	local label = Instance.new("TextLabel")
	label.Size = UDim2.fromScale(1, 1)
	label.BackgroundTransparency = 1
	label.Font = Enum.Font.FredokaOne
	label.TextScaled = true
	label.TextColor3 = Color3.new(1, 1, 1)
	label.TextStrokeTransparency = 0.3
	label.Text = text
	label.Parent = gui
	return label
end

function Build.prompt(part, objectText, actionText, distance)
	local prompt = Instance.new("ProximityPrompt")
	prompt.ObjectText = objectText
	prompt.ActionText = actionText
	prompt.KeyboardKeyCode = Enum.KeyCode.E
	prompt.MaxActivationDistance = distance or 10
	prompt.RequiresLineOfSight = false
	prompt.Parent = part
	return prompt
end

-- Table on four legs. `center.Y` is the floor top; `topHeight` is the height of the table top's centre.
function Build.table(parent, name, center, topSize, topHeight, color)
	color = color or Color3.fromRGB(104, 72, 48)
	Build.block(
		parent,
		name .. "Top",
		topSize,
		Vector3.new(center.X, center.Y + topHeight, center.Z),
		color,
		Enum.Material.Wood
	)
	local legHeight = topHeight - topSize.Y / 2
	local dx, dz = topSize.X / 2 - 0.5, topSize.Z / 2 - 0.5
	for _, sign in ipairs({ { 1, 1 }, { 1, -1 }, { -1, 1 }, { -1, -1 } }) do
		Build.block(
			parent,
			name .. "Leg",
			Vector3.new(0.6, legHeight, 0.6),
			Vector3.new(center.X + sign[1] * dx, center.Y + legHeight / 2, center.Z + sign[2] * dz),
			color,
			Enum.Material.Wood
		)
	end
end

-- Blocky Christmas tree. Branches start above gift height so presents fit underneath.
function Build.tree(parent, basePosition, padSize)
	local trunk = Build.block(
		parent,
		"TreeTrunk",
		Vector3.new(1.5, 3, 1.5),
		basePosition + Vector3.new(0, 1.5, 0),
		Color3.fromRGB(104, 72, 48),
		Enum.Material.Wood
	)
	trunk.CanQuery = false

	local tierY = basePosition.Y + 3
	for index, width in ipairs({ 8, 6, 4, 2 }) do
		Build.ghost(
			parent,
			"TreeTier" .. index,
			Vector3.new(width, 2, width),
			Vector3.new(basePosition.X, tierY + 1, basePosition.Z),
			Color3.fromRGB(40, 110 + index * 8, 60),
			Enum.Material.Fabric
		)
		tierY += 2
	end

	local star = Build.ghost(
		parent,
		"TreeStar",
		Vector3.new(1.2, 1.2, 1.2),
		Vector3.new(basePosition.X, tierY + 0.6, basePosition.Z),
		Color3.fromRGB(255, 220, 90),
		Enum.Material.Neon
	)
	local light = Build.pointLight(star, Color3.fromRGB(255, 214, 140), 1.6, 26)

	local bulbColors = {
		Color3.fromRGB(255, 80, 80),
		Color3.fromRGB(90, 170, 255),
		Color3.fromRGB(255, 200, 70),
		Color3.fromRGB(120, 255, 140),
	}
	for index, color in ipairs(bulbColors) do
		local angle = index * math.pi / 2
		local offset = Vector3.new(math.cos(angle) * 3.2, 4.5 + index * 0.6, math.sin(angle) * 3.2)
		local bulb = Build.ghost(parent, "TreeBulb", Vector3.new(0.6, 0.6, 0.6), basePosition + offset, color, Enum.Material.Neon)
		bulb.Shape = Enum.PartType.Ball
	end

	if padSize then
		local pad = Build.ghost(
			parent,
			"DeliveryPad",
			Vector3.new(padSize, 0.1, padSize),
			basePosition + Vector3.new(0, 0.06, 0),
			Color3.fromRGB(255, 214, 120),
			Enum.Material.Neon
		)
		pad.Transparency = 0.7
	end
	return light
end

function Build.applyLighting()
	Lighting.ClockTime = 0
	Lighting.Brightness = 1
	Lighting.Ambient = Color3.fromRGB(70, 62, 82)
	Lighting.OutdoorAmbient = Color3.fromRGB(58, 60, 84)
	Lighting.GlobalShadows = true
end

return Build
