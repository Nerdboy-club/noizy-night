-- Generates delivery orders for the board: an address (chimney number), a set of gifts,
-- a base reward and sometimes the rare "spooky house" modifier (darker, Grandpa sleeps lighter, tips x2).

local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Config = require(ReplicatedStorage:WaitForChild("Shared").Config)

local OrderService = {}

local EXTRA_KINDS = { "Small", "Noisy", "Fragile", "MusicBox", "RcCar", "Huge" }
local STREETS = { "Еловая", "Снежная", "Пряничная", "Морозная", "Колокольная", "Мандариновая" }

local function generateOne(usedAddresses)
	local address
	repeat
		address = math.random(1, Config.Round.ChimneyCount)
	until not usedAddresses[address]
	usedAddresses[address] = true

	local kinds = { "Small" }
	local extraCount = math.random(1, 3)
	local hasHuge = false
	for _ = 1, extraCount do
		local kind = EXTRA_KINDS[math.random(1, #EXTRA_KINDS)]
		if kind == "Huge" then
			if hasHuge then
				kind = "Small"
			end
			hasHuge = true
		end
		table.insert(kinds, kind)
	end

	local spooky = math.random() < Config.Economy.SpookyChance
	return {
		Address = address,
		Street = STREETS[math.random(1, #STREETS)],
		Kinds = kinds,
		Spooky = spooky,
		TipMultiplier = spooky and Config.Economy.SpookyTipMultiplier or 1,
		-- Bigger orders pay more.
		BaseReward = math.random(Config.Economy.RewardMin, Config.Economy.RewardMax) + (#kinds - 2) * 40,
	}
end

function OrderService.Generate(count)
	local orders = {}
	local used = {}
	for _ = 1, count do
		table.insert(orders, generateOne(used))
	end
	return orders
end

function OrderService.Describe(order)
	local names = {}
	for _, kind in ipairs(order.Kinds) do
		table.insert(names, Config.GiftKinds[kind].Name)
	end
	local lines = {
		string.format("ул. %s, труба №%d", order.Street, order.Address),
		"Подарки: " .. table.concat(names, ", "),
		string.format("Чаевые: до %d", math.floor(order.BaseReward * order.TipMultiplier)),
	}
	if order.Spooky then
		table.insert(lines, "ЖУТКИЙ ДОМ ×2")
	end
	return table.concat(lines, "\n")
end

return OrderService
