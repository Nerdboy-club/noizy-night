-- Snowy rooftops: a 5 x 3 grid of roofs at different heights, one numbered chimney on each (15 total).
-- Equal-height neighbours are separated by a gap you have to sprint-jump; different heights are
-- joined by snow ramps. Some roofs are icy, low roofs have snow piles that soften landings.

local Build = require(script.Parent.Build)

local RooftopBuilder = {}

local ORIGIN = Vector3.new(600, 80, 0)
local COLUMNS, ROWS = 5, 3
local ROOF_SIZE = 30
local GAP = 4
local PITCH = ROOF_SIZE + GAP
local ROOF_THICKNESS = 6
local HEIGHT_STEPS = { 0, 3, 6 }

local COLOR_ROOF = Color3.fromRGB(226, 232, 244)
local COLOR_ICE = Color3.fromRGB(170, 215, 245)
local COLOR_BRICK = Color3.fromRGB(150, 70, 60)

local function roofCenter(column, row)
	return ORIGIN.X + (column - (COLUMNS + 1) / 2) * PITCH, ORIGIN.Z + (row - (ROWS + 1) / 2) * PITCH
end

local function addRamp(model, lowPoint, highPoint)
	local length = (highPoint - lowPoint).Magnitude
	local ramp = Instance.new("Part")
	ramp.Name = "SnowRamp"
	ramp.Anchored = true
	ramp.Size = Vector3.new(9, 0.6, length)
	ramp.CFrame = CFrame.lookAt((lowPoint + highPoint) / 2, highPoint)
	ramp.Color = Color3.fromRGB(245, 248, 255)
	ramp.Material = Enum.Material.Snow
	ramp.TopSurface = Enum.SurfaceType.Smooth
	ramp.BottomSurface = Enum.SurfaceType.Smooth
	ramp:SetAttribute("Surface", "Carpet")
	ramp.Parent = model
end

function RooftopBuilder.Build()
	local model = Instance.new("Model")
	model.Name = "Rooftops"

	local rng = Random.new(2026)

	local numbers = {}
	for number = 1, COLUMNS * ROWS do
		numbers[number] = number
	end
	for index = #numbers, 2, -1 do
		local swap = rng:NextInteger(1, index)
		numbers[index], numbers[swap] = numbers[swap], numbers[index]
	end

	-- Heights (the start roof is always the middle step).
	local tops = {}
	for column = 1, COLUMNS do
		tops[column] = {}
		for row = 1, ROWS do
			local step = HEIGHT_STEPS[rng:NextInteger(1, #HEIGHT_STEPS)]
			if column == 1 and row == 1 then
				step = 3
			end
			tops[column][row] = ORIGIN.Y + step
		end
	end

	local chimneys = {}
	local index = 0
	for column = 1, COLUMNS do
		for row = 1, ROWS do
			index += 1
			local x, z = roofCenter(column, row)
			local top = tops[column][row]
			local isStart = column == 1 and row == 1
			local icy = not isStart and rng:NextNumber() < 0.25

			local roof = Build.floor(
				model,
				"Roof",
				Vector3.new(ROOF_SIZE, ROOF_THICKNESS, ROOF_SIZE),
				Vector3.new(x, top - ROOF_THICKNESS / 2, z),
				icy and COLOR_ICE or COLOR_ROOF,
				icy and Enum.Material.Ice or Enum.Material.Snow,
				icy and "Ice" or "Carpet"
			)
			if icy then
				roof.CustomPhysicalProperties = PhysicalProperties.new(0.9, 0.02, 0, 100, 1)
			end

			-- House body below the roof (decoration).
			Build.ghost(
				model,
				"HouseBody",
				Vector3.new(ROOF_SIZE - 4, 40, ROOF_SIZE - 4),
				Vector3.new(x, top - ROOF_THICKNESS - 20, z),
				Color3.fromRGB(120, 90, 80),
				Enum.Material.Brick
			)

			-- Chimney
			local offsetX = rng:NextInteger(-8, 8)
			local offsetZ = rng:NextInteger(4, 9)
			local number = numbers[index]
			local chimney = Build.block(
				model,
				"Chimney" .. number,
				Vector3.new(5, 7, 5),
				Vector3.new(x + offsetX, top + 3.5, z + offsetZ),
				COLOR_BRICK,
				Enum.Material.Brick
			)
			Build.ghost(
				model,
				"ChimneyHole",
				Vector3.new(3.4, 0.2, 3.4),
				Vector3.new(x + offsetX, top + 7.05, z + offsetZ),
				Color3.fromRGB(16, 14, 14)
			)
			Build.billboard(chimney, "№" .. number, 6, 140, 70)
			local prompt = Build.prompt(chimney, "Труба №" .. number, "Нырнуть в дымоход", 10)
			table.insert(chimneys, { Part = chimney, Prompt = prompt, Number = number })

			-- Snow pile that softens landings on the lowest roofs.
			if top == ORIGIN.Y then
				local pile = Build.floor(
					model,
					"SnowPile",
					Vector3.new(10, 2, 10),
					Vector3.new(x - 6, top + 1, z - 7),
					Color3.fromRGB(250, 252, 255),
					Enum.Material.Snow,
					"Carpet"
				)
				pile:SetAttribute("Soft", true)
			end
		end
	end

	-- Ramps between neighbours of different height.
	local half = ROOF_SIZE / 2
	for column = 1, COLUMNS do
		for row = 1, ROWS do
			local x, z = roofCenter(column, row)
			local top = tops[column][row]

			if column < COLUMNS then
				local otherTop = tops[column + 1][row]
				local otherX = roofCenter(column + 1, row)
				if otherTop > top then
					addRamp(model, Vector3.new(x + half - 8, top, z), Vector3.new(otherX - half + 1, otherTop, z))
				elseif otherTop < top then
					addRamp(model, Vector3.new(otherX - half + 8, otherTop, z), Vector3.new(x + half - 1, top, z))
				end
			end

			if row < ROWS then
				local otherTop = tops[column][row + 1]
				local _, otherZ = roofCenter(column, row + 1)
				if otherTop > top then
					addRamp(model, Vector3.new(x, top, z + half - 8), Vector3.new(x, otherTop, otherZ - half + 1))
				elseif otherTop < top then
					addRamp(model, Vector3.new(x, otherTop, otherZ - half + 8), Vector3.new(x, top, z + half - 1))
				end
			end
		end
	end

	Build.lamp(model, "Moon", ORIGIN + Vector3.new(0, 70, 0), Color3.fromRGB(190, 210, 255), 3, 60)

	model.Parent = workspace

	local startX, startZ = roofCenter(1, 1)
	local spawnPosition = Vector3.new(startX, tops[1][1] + 3, startZ - 8)
	return {
		Model = model,
		SpawnCFrame = CFrame.lookAt(spawnPosition, spawnPosition + Vector3.new(10, 0, 0)),
		Chimneys = chimneys,
		KillY = ORIGIN.Y - 20,
	}
end

return RooftopBuilder
