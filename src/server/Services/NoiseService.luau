-- Shared noise bus. Anything that makes a sound calls Emit; listeners (Grandpa, later the cat,
-- round stats) subscribe to NoiseEmitted and ask LoudnessAt for their own position.

local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared.Config)
local Signal = require(Shared.Signal)

local NoiseService = {}

-- Fires with: { Position: Vector3, Loudness: number, Kind: string, Player: Player? }
-- Kind is one of: "Step", "Creak", "Land", "Bump", "Gift".
NoiseService.NoiseEmitted = Signal.new()

function NoiseService.Start() end

function NoiseService.Emit(position, loudness, kind, player)
	local event = {
		Position = position,
		Loudness = math.clamp(loudness, 0, 100),
		Kind = kind,
		Player = player,
	}
	NoiseService.NoiseEmitted:Fire(event)
	return event
end

-- Linear falloff: silent beyond (loudness / FalloffDivisor) metres.
function NoiseService.LoudnessAt(event, listenerPosition)
	local distanceMeters = (event.Position - listenerPosition).Magnitude / Config.StudsPerMeter
	local rangeMeters = math.max(1, event.Loudness / Config.Noise.FalloffDivisor)
	return event.Loudness * math.clamp(1 - distanceMeters / rangeMeters, 0, 1)
end

return NoiseService
