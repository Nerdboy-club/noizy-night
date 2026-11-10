-- Floppy ragdoll: swaps every Motor6D (except the root joint) for a BallSocketConstraint.
-- Comedy over realism. Works for R15 and R6 because it only looks at Motor6Ds.

local CollectionService = game:GetService("CollectionService")

local Ragdoll = {}

local TAG = "RagdollPiece"
local ROOT_JOINTS = { Root = true, RootJoint = true }

function Ragdoll.IsEnabled(character)
	return character:GetAttribute("Ragdolled") == true
end

function Ragdoll.Enable(character)
	local humanoid = character:FindFirstChildOfClass("Humanoid")
	if not humanoid or Ragdoll.IsEnabled(character) then
		return
	end
	character:SetAttribute("Ragdolled", true)

	for _, descendant in ipairs(character:GetDescendants()) do
		if
			descendant:IsA("Motor6D")
			and not ROOT_JOINTS[descendant.Name]
			and descendant.Enabled
			and descendant.Part0
			and descendant.Part1
		then
			local attachment0 = Instance.new("Attachment")
			attachment0.Name = "RagdollAttachment0"
			attachment0.CFrame = descendant.C0
			attachment0.Parent = descendant.Part0

			local attachment1 = Instance.new("Attachment")
			attachment1.Name = "RagdollAttachment1"
			attachment1.CFrame = descendant.C1
			attachment1.Parent = descendant.Part1

			local socket = Instance.new("BallSocketConstraint")
			socket.Name = "RagdollSocket"
			socket.Attachment0 = attachment0
			socket.Attachment1 = attachment1
			socket.LimitsEnabled = true
			socket.UpperAngle = 50
			socket.TwistLimitsEnabled = true
			socket.TwistLowerAngle = -35
			socket.TwistUpperAngle = 35
			socket.Parent = descendant.Part1

			CollectionService:AddTag(attachment0, TAG)
			CollectionService:AddTag(attachment1, TAG)
			CollectionService:AddTag(socket, TAG)

			descendant:SetAttribute("RagdollDisabled", true)
			descendant.Enabled = false
		end
	end

	humanoid.PlatformStand = true
end

function Ragdoll.Disable(character)
	if not Ragdoll.IsEnabled(character) then
		return
	end
	character:SetAttribute("Ragdolled", false)

	for _, descendant in ipairs(character:GetDescendants()) do
		if descendant:IsA("Motor6D") and descendant:GetAttribute("RagdollDisabled") then
			descendant:SetAttribute("RagdollDisabled", nil)
			descendant.Enabled = true
		elseif CollectionService:HasTag(descendant, TAG) then
			descendant:Destroy()
		end
	end

	local humanoid = character:FindFirstChildOfClass("Humanoid")
	if humanoid then
		humanoid.PlatformStand = false
	end
end

return Ragdoll
