-- Small helper that walks a Humanoid along a PathfindingService path.
-- Falls back to walking straight at the destination when no path is found.

local PathfindingService = game:GetService("PathfindingService")

local PathAgent = {}
PathAgent.__index = PathAgent

local function flatDistance(a, b)
	return Vector3.new(a.X - b.X, 0, a.Z - b.Z).Magnitude
end

-- options: FloorY, ArriveDistance, AgentRadius, AgentHeight
function PathAgent.new(humanoid, root, options)
	return setmetatable({
		Humanoid = humanoid,
		Root = root,
		FloorY = options.FloorY,
		ArriveDistance = options.ArriveDistance or 2,
		AgentParameters = {
			AgentRadius = options.AgentRadius or 2,
			AgentHeight = options.AgentHeight or 5,
			AgentCanJump = false,
		},
		Destination = nil,
		Waypoints = nil,
		WaypointIndex = 1,
		Request = 0,
	}, PathAgent)
end

-- Yields while the path is computed.
function PathAgent:SetDestination(target)
	local goal = Vector3.new(target.X, self.FloorY, target.Z)
	self.Destination = goal
	self.Waypoints = nil
	self.WaypointIndex = 1
	self.Request += 1
	local request = self.Request

	local start = Vector3.new(self.Root.Position.X, self.FloorY, self.Root.Position.Z)
	local path = PathfindingService:CreatePath(self.AgentParameters)
	local ok = pcall(function()
		path:ComputeAsync(start, goal)
	end)
	if request ~= self.Request then
		return
	end
	if ok and path.Status == Enum.PathStatus.Success then
		self.Waypoints = path:GetWaypoints()
		self.WaypointIndex = 2
	end
end

function PathAgent:Stop()
	self.Destination = nil
	self.Waypoints = nil
	self.Request += 1
	self.Humanoid:MoveTo(self.Root.Position)
end

-- Call regularly. Returns true once the agent has arrived (or has no destination).
function PathAgent:Update()
	local destination = self.Destination
	if not destination then
		return true
	end
	local position = self.Root.Position
	if flatDistance(destination, position) <= self.ArriveDistance then
		return true
	end

	local goal = destination
	local waypoints = self.Waypoints
	if waypoints then
		local waypoint = waypoints[self.WaypointIndex]
		while waypoint and flatDistance(waypoint.Position, position) < 2.5 do
			self.WaypointIndex += 1
			waypoint = waypoints[self.WaypointIndex]
		end
		if waypoint then
			goal = waypoint.Position
		end
	end
	self.Humanoid:MoveTo(goal)
	return false
end

return PathAgent
