-- The sleeping house, six rooms (top view, +Z is north, studs):
--
--   z 26..50 | Study  | Bedroom (Grandpa) | Garage (ice puddle)
--   z 16..26 | Study  | Hallway (creaky)  | Garage
--   z -16..16|        | Living (tree,     | Kitchen (tile, cat)
--            |        |  fireplace)       |
--              x -44..-20   x -20..20        x 20..44
--
-- Furniture is human-sized (Grandpa is 8 studs tall, elves about 5), so sneaking elves fit under tables.

local CollectionService = game:GetService("CollectionService")

local Build = require(script.Parent.Build)

local HouseBuilder = {}

local FLOOR_TOP = 0.5
local WALL_HEIGHT = 12
local DOOR_HEIGHT = 9

local COLOR_WALL = Color3.fromRGB(214, 196, 170)
local COLOR_CARPET = Color3.fromRGB(150, 44, 48)
local COLOR_WOOD = Color3.fromRGB(140, 96, 60)
local COLOR_TILE = Color3.fromRGB(220, 222, 226)
local COLOR_CONCRETE = Color3.fromRGB(128, 128, 132)
local COLOR_FURNITURE = Color3.fromRGB(104, 72, 48)

local function at(x, y, z)
	return Vector3.new(x, FLOOR_TOP + y, z)
end

local function hidingSpot(model, name, label, size, position)
	local zone = Build.ghost(model, name, size, position, Color3.new(1, 1, 1))
	zone.Transparency = 1
	zone:SetAttribute("Label", label)
	CollectionService:AddTag(zone, "HidingSpot")
	return zone
end

local function toy(model, position, color)
	local part = Build.ghost(model, "Toy", Vector3.new(1, 0.8, 1.4), position, color, Enum.Material.SmoothPlastic)
	CollectionService:AddTag(part, "TripHazard")
	return part
end

-- Swinging door hinged on its low-X edge, in a wall that runs along X.
local function door(model, name, hingeX, z, width)
	local height = DOOR_HEIGHT - 0.6
	local centerY = FLOOR_TOP + 0.3 + height / 2

	local post = Build.ghost(model, name .. "Post", Vector3.new(0.3, height, 0.3), Vector3.new(hingeX - 0.15, centerY, z), COLOR_FURNITURE)

	local panel = Instance.new("Part")
	panel.Name = name
	panel.Size = Vector3.new(width - 0.5, height, 0.4)
	panel.Position = Vector3.new(hingeX + 0.25 + (width - 0.5) / 2, centerY, z)
	panel.Color = Color3.fromRGB(120, 84, 56)
	panel.Material = Enum.Material.Wood
	panel.TopSurface = Enum.SurfaceType.Smooth
	panel.BottomSurface = Enum.SurfaceType.Smooth
	panel.CustomPhysicalProperties = PhysicalProperties.new(0.25, 0.3, 0.1)
	panel.Parent = model

	-- Hinge axis is the attachment's X axis; rotate it to point up.
	local hingeCFrame = CFrame.new(hingeX, centerY, z) * CFrame.Angles(0, 0, math.rad(90))
	local postAttachment = Instance.new("Attachment")
	postAttachment.CFrame = post.CFrame:ToObjectSpace(hingeCFrame)
	postAttachment.Parent = post
	local panelAttachment = Instance.new("Attachment")
	panelAttachment.CFrame = panel.CFrame:ToObjectSpace(hingeCFrame)
	panelAttachment.Parent = panel

	local hinge = Instance.new("HingeConstraint")
	hinge.Attachment0 = postAttachment
	hinge.Attachment1 = panelAttachment
	hinge.LimitsEnabled = true
	hinge.LowerAngle = -100
	hinge.UpperAngle = 100
	-- Weak servo that slowly swings the door shut again.
	hinge.ActuatorType = Enum.ActuatorType.Servo
	hinge.TargetAngle = 0
	hinge.AngularSpeed = 1.5
	hinge.ServoMaxTorque = 600
	hinge.Parent = panel

	return panel
end

function HouseBuilder.Build()
	local model = Instance.new("Model")
	model.Name = "House"

	local lights = {}
	local function lamp(name, position, color, brightness, range)
		local light = Build.lamp(model, name, position, color, brightness, range)
		table.insert(lights, { Light = light, Brightness = brightness, Color = color })
	end

	-- Floors
	Build.floor(model, "LivingFloor", Vector3.new(40, 1, 32), Vector3.new(0, 0, 0), COLOR_CARPET, Enum.Material.Fabric, "Carpet")
	Build.floor(model, "HallFloor", Vector3.new(40, 1, 10), Vector3.new(0, 0, 21), COLOR_WOOD, Enum.Material.WoodPlanks, "Wood")
	Build.floor(model, "BedroomFloor", Vector3.new(40, 1, 24), Vector3.new(0, 0, 38), COLOR_WOOD, Enum.Material.WoodPlanks, "Wood")
	Build.floor(model, "KitchenFloor", Vector3.new(24, 1, 32), Vector3.new(32, 0, 0), COLOR_TILE, Enum.Material.Marble, "Tile")
	Build.floor(model, "GarageFloor", Vector3.new(24, 1, 34), Vector3.new(32, 0, 33), COLOR_CONCRETE, Enum.Material.Concrete, "Tile")
	Build.floor(model, "StudyFloor", Vector3.new(24, 1, 34), Vector3.new(-32, 0, 33), Color3.fromRGB(70, 96, 80), Enum.Material.Fabric, "Carpet")

	Build.floor(model, "CreakyPlanks", Vector3.new(6, 0.1, 10), at(-1, 0.05, 21), Color3.fromRGB(112, 74, 44), Enum.Material.WoodPlanks, "Wood", true)
	Build.floor(model, "CreakyPlanks", Vector3.new(10, 0.1, 5), at(-2, 0.05, 31), Color3.fromRGB(112, 74, 44), Enum.Material.WoodPlanks, "Wood", true)
	Build.floor(model, "BedroomRug", Vector3.new(8, 0.1, 10), at(3, 0.05, 42), Color3.fromRGB(70, 90, 140), Enum.Material.Fabric, "Carpet")
	local ice = Build.floor(model, "IcePuddle", Vector3.new(14, 0.1, 12), at(32, 0.05, 34), Color3.fromRGB(180, 225, 250), Enum.Material.Ice, "Ice")
	ice.CustomPhysicalProperties = PhysicalProperties.new(0.9, 0.02, 0, 100, 1)

	-- Walls
	local wall = { BaseY = FLOOR_TOP, Height = WALL_HEIGHT, DoorHeight = DOOR_HEIGHT, Color = COLOR_WALL }
	local function walls(x1, z1, x2, z2, doors)
		local options = table.clone(wall)
		options.Doors = doors
		Build.wall(model, "Wall", x1, z1, x2, z2, options)
	end
	walls(-20, -16, 44, -16)
	walls(-44, 50, 44, 50)
	walls(-44, 16, -44, 50)
	walls(44, -16, 44, 50)
	walls(-20, -16, -20, 50, { { 18, 24 } })
	walls(20, -16, 20, 50, { { -3, 3 }, { 18, 24 } })
	walls(-44, 16, 44, 16, { { 8, 14 }, { 29, 35 } })
	walls(-20, 26, 20, 26, { { -14, -8 } })

	-- Creaky doors (as in the Unity build: hall <-> living, hall <-> bedroom)
	local doors = {
		door(model, "LivingDoor", 8, 16, 6),
		door(model, "BedroomDoor", -14, 26, 6),
	}

	-- Living room: fireplace (spawn / exit), tree (delivery), dining table (hiding spot)
	local fireplace = Build.block(model, "Fireplace", Vector3.new(8, 8, 2), at(-10, 4, -15), Color3.fromRGB(120, 70, 60), Enum.Material.Brick)
	Build.ghost(model, "Firebox", Vector3.new(4, 4, 0.2), at(-10, 2, -13.95), Color3.fromRGB(20, 16, 16))
	lamp("Embers", at(-10, 1, -13.4), Color3.fromRGB(255, 130, 50), 2, 24)

	local treePosition = Vector3.new(12, FLOOR_TOP, -8)
	local treeLight = Build.tree(model, treePosition, 12)
	table.insert(lights, { Light = treeLight, Brightness = treeLight.Brightness, Color = treeLight.Color })

	Build.table(model, "DiningTable", Vector3.new(-2, FLOOR_TOP, 4), Vector3.new(9, 0.5, 5), 5)
	local hidingZones = {
		hidingSpot(model, "HideDiningTable", "обеденным столом", Vector3.new(9, 4.6, 5), at(-2, 2.3, 4)),
	}
	Build.block(model, "Sofa", Vector3.new(10, 3, 4), at(-10, 1.5, 12), Color3.fromRGB(70, 110, 90), Enum.Material.Fabric)

	-- Hallway
	Build.block(model, "GrandfatherClock", Vector3.new(3, 10, 2), at(-17.5, 5, 21), COLOR_FURNITURE, Enum.Material.Wood)
	lamp("HallLamp", at(14, 9, 21), Color3.fromRGB(255, 200, 150), 0.8, 18)

	-- Kitchen: counter, table (hiding spot), cat bed and bowl
	Build.block(model, "KitchenCounter", Vector3.new(4, 5, 22), at(41.5, 2.5, -2), Color3.fromRGB(180, 150, 120), Enum.Material.Wood)
	Build.table(model, "KitchenTable", Vector3.new(29, FLOOR_TOP, -7), Vector3.new(8, 0.5, 6), 5)
	table.insert(hidingZones, hidingSpot(model, "HideKitchenTable", "кухонным столом", Vector3.new(8, 4.6, 6), at(29, 2.3, -7)))
	Build.ghost(model, "CatBed", Vector3.new(4, 0.5, 4), at(24, 0.25, 12), Color3.fromRGB(200, 120, 140), Enum.Material.Fabric)
	Build.ghost(model, "CatBowl", Vector3.new(1.6, 0.5, 1.6), at(37, 0.25, 12), Color3.fromRGB(90, 150, 220), Enum.Material.SmoothPlastic)
	lamp("KitchenLamp", at(32, 10, 0), Color3.fromRGB(255, 236, 200), 0.9, 26)

	-- Study: cramped, shelves leave a narrow zigzag
	Build.block(model, "Bookshelf", Vector3.new(2, 10, 14), at(-28, 5, 28), COLOR_FURNITURE, Enum.Material.Wood)
	Build.block(model, "Bookshelf", Vector3.new(2, 10, 14), at(-36, 5, 40), COLOR_FURNITURE, Enum.Material.Wood)
	Build.block(model, "Desk", Vector3.new(8, 4, 4), at(-26, 2, 46.5), COLOR_FURNITURE, Enum.Material.Wood)
	lamp("StudyLamp", at(-32, 9, 33), Color3.fromRGB(180, 255, 200), 0.7, 22)

	-- Garage: car, crates, ice puddle
	Build.block(model, "Car", Vector3.new(8, 5, 14), at(39.5, 2.5, 24), Color3.fromRGB(170, 50, 50), Enum.Material.Metal)
	Build.block(model, "Crate", Vector3.new(4, 4, 4), at(23, 2, 46), COLOR_FURNITURE, Enum.Material.WoodPlanks)
	Build.block(model, "Crate", Vector3.new(3, 3, 3), at(27, 1.5, 47), COLOR_FURNITURE, Enum.Material.WoodPlanks)
	lamp("GarageLamp", at(32, 10, 36), Color3.fromRGB(200, 220, 255), 0.7, 26)

	-- Bedroom: bed on legs (hiding spot underneath), wardrobe, night light
	local bedTop = FLOOR_TOP + 6
	Build.table(model, "Bed", Vector3.new(12, FLOOR_TOP, 43), Vector3.new(8, 1.5, 12), 5.25, Color3.fromRGB(90, 110, 170))
	Build.ghost(model, "Pillow", Vector3.new(5, 0.6, 2), Vector3.new(12, bedTop + 0.3, 47.5), Color3.fromRGB(235, 235, 240), Enum.Material.Fabric)
	table.insert(hidingZones, hidingSpot(model, "HideBed", "кроватью Деда", Vector3.new(8, 4.4, 12), at(12, 2.2, 43)))
	Build.block(model, "Wardrobe", Vector3.new(6, 10, 3), at(-16, 5, 47.5), COLOR_FURNITURE, Enum.Material.Wood)
	lamp("NightLight", at(18, 3, 30), Color3.fromRGB(120, 150, 255), 0.6, 16)

	-- Toys on the floor
	local toys = {
		toy(model, at(6, 0.4, 9), Color3.fromRGB(240, 80, 80)),
		toy(model, at(-13, 0.4, -2), Color3.fromRGB(80, 160, 240)),
		toy(model, at(10, 0.4, 20), Color3.fromRGB(250, 210, 70)),
		toy(model, at(-7, 0.4, 35), Color3.fromRGB(120, 220, 130)),
		toy(model, at(28, 0.4, 8), Color3.fromRGB(240, 140, 60)),
		toy(model, at(-30, 0.4, 20), Color3.fromRGB(200, 110, 220)),
	}

	model.Parent = workspace

	local function setMood(spooky)
		for _, entry in ipairs(lights) do
			if spooky then
				entry.Light.Brightness = entry.Brightness * 0.35
				entry.Light.Color = entry.Color:Lerp(Color3.fromRGB(120, 90, 200), 0.6)
			else
				entry.Light.Brightness = entry.Brightness
				entry.Light.Color = entry.Color
			end
		end
	end

	local spawnPosition = Vector3.new(-10, FLOOR_TOP + 3, -10)
	local sitPosition = Vector3.new(12, bedTop + 5, 44)

	return {
		Model = model,
		FloorY = FLOOR_TOP,
		SpawnCFrame = CFrame.lookAt(spawnPosition, spawnPosition + Vector3.new(0, 0, 10)),
		Fireplace = fireplace,
		TreePosition = treePosition,
		DeliveryRadius = 7,
		SetMood = setMood,
		HidingZones = hidingZones,
		Toys = toys,
		Doors = doors,
		-- Gifts "tumble down the chimney network" and land around the house.
		HugeGiftSpawn = CFrame.new(at(26, 2.2, 40)),
		GiftSpawns = {
			CFrame.new(at(2, 1.3, 47)), -- bedroom, by Grandpa's bed
			CFrame.new(at(-40, 1.3, 46)), -- study, far corner
			CFrame.new(at(38, 1.3, 46)), -- garage, behind the ice
			CFrame.new(at(38, 1.3, -13)), -- kitchen
			CFrame.new(at(16, 1.3, 21)), -- hallway
			CFrame.new(at(-24, 1.3, 20)), -- study entrance
		},
		CatBedPosition = at(24, 0, 12),
		CatBowlPosition = at(37, 0, 9),
		-- Grandpa root poses: lying on his back, sitting up (facing the bedroom door), standing next to the bed.
		BedLieCFrame = CFrame.new(12, bedTop + 1, 43) * CFrame.Angles(math.rad(90), 0, 0),
		BedSitCFrame = CFrame.lookAt(sitPosition, Vector3.new(-11, sitPosition.Y, 26)),
		BedStandPosition = Vector3.new(5, FLOOR_TOP, 43),
	}
end

return HouseBuilder
