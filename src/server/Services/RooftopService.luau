-- Rooftop stage: chimney prompts and putting elves who fell off back on the first roof.

local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Remotes = require(ReplicatedStorage:WaitForChild("Shared").Remotes)

local ElfService = require(script.Parent.ElfService)
local MissionService = require(script.Parent.MissionService)

local RooftopService = {}

function RooftopService.Start(world)
	local rooftop = world.Rooftop

	for _, chimney in ipairs(rooftop.Chimneys) do
		chimney.Prompt.Triggered:Connect(function(player)
			MissionService.TryChimney(player, chimney.Number)
		end)
	end

	task.spawn(function()
		while true do
			task.wait(0.5)
			if MissionService.GetPhase() == "Rooftop" then
				for _, player in ipairs(MissionService.GetCrew()) do
					local character = player.Character
					local root = character and character:FindFirstChild("HumanoidRootPart")
					if root and root.Position.Y < rooftop.KillY then
						root.AssemblyLinearVelocity = Vector3.zero
						ElfService.Recover(player)
						task.spawn(ElfService.Teleport, player, rooftop.SpawnCFrame)
						Remotes.Toast:FireClient(player, "Плюх! Обратно на крышу.")
					end
				end
			end
		end
	end)
end

return RooftopService
