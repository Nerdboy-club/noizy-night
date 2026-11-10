-- Dresses the player's avatar as an elf: pointy hat with a pom-pom and a scarf, coloured by the
-- equipped skin. Purely cosmetic. Re-applied on spawn and whenever the skin changes.

local Players = game:GetService("Players")

local ProfileService = require(script.Parent.ProfileService)

local SkinService = {}

local FOLDER_NAME = "ElfSkin"

local function piece(folder, anchorPart, name, size, offset, color, shape)
	local part = Instance.new("Part")
	part.Name = name
	part.Size = size
	part.Color = color
	part.Material = Enum.Material.Fabric
	part.TopSurface = Enum.SurfaceType.Smooth
	part.BottomSurface = Enum.SurfaceType.Smooth
	part.CanCollide = false
	part.CanQuery = false
	part.CanTouch = false
	part.Massless = true
	if shape then
		part.Shape = shape
	end
	part.CFrame = anchorPart.CFrame * offset

	local weld = Instance.new("WeldConstraint")
	weld.Part0 = anchorPart
	weld.Part1 = part
	weld.Parent = part
	part.Parent = folder
	return part
end

function SkinService.Apply(player)
	local character = player.Character
	local head = character and character:FindFirstChild("Head")
	if not head then
		return
	end

	local old = character:FindFirstChild(FOLDER_NAME)
	if old then
		old:Destroy()
	end

	local skin = ProfileService.GetSkin(player)
	local folder = Instance.new("Folder")
	folder.Name = FOLDER_NAME

	local top = head.Size.Y / 2
	piece(folder, head, "HatBrim", Vector3.new(1.5, 0.35, 1.5), CFrame.new(0, top + 0.1, 0), skin.Scarf)
	piece(folder, head, "HatBase", Vector3.new(1.3, 0.7, 1.3), CFrame.new(0, top + 0.6, 0), skin.Hat)
	piece(folder, head, "HatTip", Vector3.new(0.8, 0.7, 0.8), CFrame.new(0, top + 1.25, 0.15), skin.Hat)
	local pom = piece(folder, head, "HatPom", Vector3.new(0.6, 0.6, 0.6), CFrame.new(0, top + 1.75, 0.3), skin.Scarf, Enum.PartType.Ball)
	piece(folder, head, "Scarf", Vector3.new(1.5, 0.4, 1.5), CFrame.new(0, -top - 0.15, 0), skin.Scarf)

	if skin.Glow then
		pom.Material = Enum.Material.Neon
		local light = Instance.new("PointLight")
		light.Color = skin.Glow
		light.Brightness = 1.2
		light.Range = 12
		light.Parent = pom
	end

	if ProfileService.HasPass(player, "Vip") then
		local gui = Instance.new("BillboardGui")
		gui.Name = "VipTag"
		gui.Size = UDim2.fromOffset(70, 26)
		gui.StudsOffset = Vector3.new(0, 3.4, 0)
		gui.Adornee = head
		gui.Parent = folder
		local label = Instance.new("TextLabel")
		label.Size = UDim2.fromScale(1, 1)
		label.BackgroundTransparency = 1
		label.Font = Enum.Font.FredokaOne
		label.TextScaled = true
		label.TextColor3 = Color3.fromRGB(255, 215, 90)
		label.TextStrokeTransparency = 0.3
		label.Text = "VIP"
		label.Parent = gui
	end

	folder.Parent = character
end

function SkinService.Start()
	local function onPlayer(player)
		player.CharacterAppearanceLoaded:Connect(function()
			SkinService.Apply(player)
		end)
		if player.Character then
			task.spawn(SkinService.Apply, player)
		end
	end

	Players.PlayerAdded:Connect(onPlayer)
	for _, player in ipairs(Players:GetPlayers()) do
		onPlayer(player)
	end

	ProfileService.SkinChanged:Connect(SkinService.Apply)
end

return SkinService
