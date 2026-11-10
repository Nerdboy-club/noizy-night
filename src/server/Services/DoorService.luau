-- Creaky doors: push one slowly and it stays quiet, walk through and it creaks,
-- barge through at a run and it slams loudly enough to wake the whole house.

local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared.Config)
local Remotes = require(Shared.Remotes)

local NoiseService = require(script.Parent.NoiseService)

local DoorConfig = Config.Door

local DoorService = {}

function DoorService.Start(world)
	local doors = {}
	for _, panel in ipairs(world.House.Doors) do
		table.insert(doors, { Panel = panel, NextCreakAt = 0, NextSlamAt = 0 })
	end

	task.spawn(function()
		while true do
			task.wait(0.1)
			local now = os.clock()
			for _, door in ipairs(doors) do
				local spin = math.abs(door.Panel.AssemblyAngularVelocity.Y)
				if spin >= DoorConfig.SlamAngularSpeed and now >= door.NextSlamAt then
					door.NextSlamAt = now + DoorConfig.SlamCooldown
					door.NextCreakAt = now + DoorConfig.SlamCooldown
					NoiseService.Emit(door.Panel.Position, Config.Noise.DoorSlam, "DoorSlam", nil)
					Remotes.Toast:FireAllClients("БАХ! Дверь хлопнула на весь дом!")
				elseif spin >= DoorConfig.CreakAngularSpeed and now >= door.NextCreakAt then
					door.NextCreakAt = now + DoorConfig.CreakCooldown
					NoiseService.Emit(door.Panel.Position, Config.Noise.DoorCreak, "DoorCreak", nil)
				end
			end
		end
	end)
end

return DoorService
