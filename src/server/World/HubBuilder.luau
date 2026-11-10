-- Santa's workshop: order board, gift conveyor, sleigh, workbench and the door back to the lobby.

local Build = require(script.Parent.Build)

local HubBuilder = {}

local ORIGIN = Vector3.new(-600, 0, 0)
local FLOOR_TOP = ORIGIN.Y + 0.5
local HALF_X, HALF_Z = 40, 30

local function at(x, y, z)
	return Vector3.new(ORIGIN.X + x, FLOOR_TOP + y, ORIGIN.Z + z)
end

function HubBuilder.Build()
	local model = Instance.new("Model")
	model.Name = "Hub"

	Build.floor(
		model,
		"Floor",
		Vector3.new(HALF_X * 2, 1, HALF_Z * 2),
		ORIGIN,
		Color3.fromRGB(150, 104, 66),
		Enum.Material.WoodPlanks,
		"Wood"
	)

	local wall = { BaseY = FLOOR_TOP, Height = 16, Color = Color3.fromRGB(170, 60, 56), Material = Enum.Material.Brick }
	local x0, x1 = ORIGIN.X - HALF_X, ORIGIN.X + HALF_X
	local z0, z1 = ORIGIN.Z - HALF_Z, ORIGIN.Z + HALF_Z
	Build.wall(model, "Wall", x0, z0, x1, z0, wall)
	Build.wall(model, "Wall", x0, z1, x1, z1, wall)
	Build.wall(model, "Wall", x0, z0, x0, z1, wall)
	Build.wall(model, "Wall", x1, z0, x1, z1, wall)

	-- Order board on the north wall (Front faces -Z, into the room).
	local board = Build.block(model, "OrderBoard", Vector3.new(36, 3, 0.6), at(0, 13, HALF_Z - 1), Color3.fromRGB(70, 46, 30), Enum.Material.Wood)
	local boardLabel = Build.sign(board, Enum.NormalId.Front, "ДОСКА ЗАКАЗОВ", Color3.fromRGB(255, 230, 180))

	local cards = {}
	for index = 1, 3 do
		local card = Build.block(
			model,
			"OrderCard" .. index,
			Vector3.new(10, 8, 0.4),
			at((index - 2) * 12, 7, HALF_Z - 1),
			Color3.fromRGB(245, 235, 210),
			Enum.Material.SmoothPlastic
		)
		local label = Build.sign(card, Enum.NormalId.Front, "", Color3.fromRGB(50, 34, 24))
		local prompt = Build.prompt(card, "Заказ " .. index, "Взять заказ", 12)
		table.insert(cards, { Part = card, Label = label, Prompt = prompt })
	end

	-- Conveyor along the west wall, moving towards +Z into an end stop.
	Build.block(model, "ConveyorBase", Vector3.new(5, 2, 30), at(-33, 1, 0), Color3.fromRGB(90, 90, 100), Enum.Material.Metal)
	local belt = Build.block(model, "ConveyorBelt", Vector3.new(5, 0.6, 30), at(-33, 2.3, 0), Color3.fromRGB(40, 40, 46), Enum.Material.Fabric)
	belt.AssemblyLinearVelocity = Vector3.new(0, 0, 5)
	Build.block(model, "ConveyorStop", Vector3.new(5, 3, 1), at(-33, 4, 15.5), Color3.fromRGB(200, 60, 60), Enum.Material.Metal)
	Build.block(model, "ConveyorRail", Vector3.new(0.4, 1.2, 30), at(-35.7, 3.2, 0), Color3.fromRGB(200, 60, 60), Enum.Material.Metal)
	Build.block(model, "ConveyorHatch", Vector3.new(6, 6, 2), at(-33, 5.6, -16), Color3.fromRGB(60, 60, 70), Enum.Material.Metal)
	Build.billboard(belt, "Конвейер подарков", 6, 240, 50)

	-- Sleigh on the east side
	local sleigh = Build.block(model, "Sleigh", Vector3.new(10, 3, 18), at(28, 2, 0), Color3.fromRGB(190, 40, 46), Enum.Material.Wood)
	Build.block(model, "SleighRunner", Vector3.new(1, 0.5, 20), at(24, 0.25, 0), Color3.fromRGB(230, 200, 90), Enum.Material.Metal)
	Build.block(model, "SleighRunner", Vector3.new(1, 0.5, 20), at(32, 0.25, 0), Color3.fromRGB(230, 200, 90), Enum.Material.Metal)
	Build.block(model, "SleighBack", Vector3.new(10, 5, 1), at(28, 4.5, -9.5), Color3.fromRGB(160, 30, 36), Enum.Material.Wood)
	local sleighLabel = Build.billboard(sleigh, "Сани", 8, 260, 60)
	local sleighPrompt = Build.prompt(sleigh, "Сани", "Загрузить подарки", 14)

	local sleighSlots = {}
	for index = 1, 6 do
		local column = (index - 1) % 2
		local row = math.floor((index - 1) / 2)
		table.insert(sleighSlots, CFrame.new(at(26 + column * 4, 5.2, -5 + row * 5)))
	end

	-- Workbench on the south wall
	local workbench = Build.block(model, "Workbench", Vector3.new(14, 4, 4), at(-8, 2, -HALF_Z + 3), Color3.fromRGB(104, 72, 48), Enum.Material.Wood)
	Build.billboard(workbench, "Верстак и магазин", 5, 260, 50)
	local workbenchPrompt = Build.prompt(workbench, "Верстак", "Прокачка и скины", 12)

	-- Door back to the lobby
	local exitDoor = Build.block(model, "LobbyDoor", Vector3.new(7, 10, 0.6), at(18, 5, -HALF_Z + 0.8), Color3.fromRGB(70, 110, 170), Enum.Material.Wood)
	Build.billboard(exitDoor, "В лобби", 6, 200, 50)
	local exitPrompt = Build.prompt(exitDoor, "Дверь", "Вернуться в лобби", 10)

	for _, offset in ipairs({ Vector3.new(-20, 13, -12), Vector3.new(20, 13, -12), Vector3.new(-20, 13, 14), Vector3.new(20, 13, 14) }) do
		Build.lamp(model, "Lamp", at(offset.X, offset.Y, offset.Z), Color3.fromRGB(255, 210, 150), 1.6, 46)
	end

	model.Parent = workspace

	local spawnPosition = at(0, 3, -12)
	return {
		Model = model,
		SpawnCFrame = CFrame.lookAt(spawnPosition, spawnPosition + Vector3.new(0, 0, 10)),
		BoardLabel = boardLabel,
		Cards = cards,
		BeltSpawnCFrame = CFrame.new(at(-33, 5, -11)),
		Sleigh = sleigh,
		SleighLabel = sleighLabel,
		SleighPrompt = sleighPrompt,
		SleighSlots = sleighSlots,
		WorkbenchPrompt = workbenchPrompt,
		ExitPrompt = exitPrompt,
	}
end

return HubBuilder
