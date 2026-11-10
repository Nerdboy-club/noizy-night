-- Minimal signal for communication between services (no instances, no table copying).

local Signal = {}
Signal.__index = Signal

function Signal.new()
	return setmetatable({ _connections = {} }, Signal)
end

function Signal:Connect(handler)
	local connections = self._connections
	local connection = { Connected = true, _handler = handler }

	function connection.Disconnect()
		if not connection.Connected then
			return
		end
		connection.Connected = false
		local index = table.find(connections, connection)
		if index then
			table.remove(connections, index)
		end
	end

	table.insert(connections, connection)
	return connection
end

function Signal:Fire(...)
	for _, connection in ipairs(table.clone(self._connections)) do
		if connection.Connected then
			task.spawn(connection._handler, ...)
		end
	end
end

return Signal
