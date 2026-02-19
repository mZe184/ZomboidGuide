require "ISUI/ISTextBox"

local ZGMB = {}

ZGMB.Endpoint = "http://127.0.0.1:8765/api/multi-base/scan"
ZGMB.ScanIntervalSeconds = 2
ZGMB.ModDataKey = "ZomboidGuideCompanion"
ZGMB.LastScanEpoch = 0
ZGMB.QueueFilePath = "ZomboidGuideCompanion/snapshots.ndjson"
ZGMB.LastTransportErrorEpoch = 0
ZGMB.TransportErrorIntervalSeconds = 12
ZGMB.LastHandledHotkey = -1
ZGMB.LastHandledHotkeyEpoch = 0
ZGMB.LastKeyTraceEpoch = 0
ZGMB.LastQueueWriteLogEpoch = 0
ZGMB.LastHttpWriteLogEpoch = 0
ZGMB.WasF8Down = false
ZGMB.WasF7Down = false
ZGMB.LastScanHeartbeatEpoch = 0
ZGMB.LastNoPlayerLogEpoch = 0
print("[ZGMB] ZomboidGuideCompanion client loaded")

local function nowEpoch()
    return os.time()
end

local pollHotkeysByState = nil

local function ensureTextBoxClass()
    if ISTextBox ~= nil and ISTextBox.new ~= nil then
        return true
    end

    pcall(function()
        require "ISUI/ISTextBox"
    end)

    return ISTextBox ~= nil and ISTextBox.new ~= nil
end

local function getState()
    local state = ModData.getOrCreate(ZGMB.ModDataKey)
    state.bases = state.bases or {}
    return state
end

local function playerOrNil()
    local player = nil
    if getSpecificPlayer ~= nil then
        local ok0, p0 = pcall(function()
            return getSpecificPlayer(0)
        end)
        if ok0 and p0 ~= nil then
            player = p0
        end

        if player == nil then
            for idx = 1, 3 do
                local okN, pN = pcall(function()
                    return getSpecificPlayer(idx)
                end)
                if okN and pN ~= nil then
                    player = pN
                    break
                end
            end
        end
    end

    if player == nil and getPlayer ~= nil then
        local okPlayer, p = pcall(function()
            return getPlayer()
        end)
        if okPlayer and p ~= nil then
            player = p
        end
    end

    if player == nil then
        return nil
    end

    if player.isDead ~= nil and player:isDead() then
        return nil
    end

    return player
end

local function buildingForPlayer(player)
    if player == nil then
        return nil
    end

    local square = player:getSquare()
    if square == nil then
        return nil
    end

    return square:getBuilding()
end

local function getBuildingId(building)
    if building == nil then
        return ""
    end

    local def = building:getDef()
    if def == nil then
        return ""
    end

    return tostring(def:getX()) .. ":" .. tostring(def:getY()) .. ":" .. tostring(def:getX2()) .. ":" .. tostring(def:getY2())
end

local function activeRunKey()
    local mode = ""
    local map = ""
    local world = getWorld()
    if world ~= nil then
        if world.getGameMode ~= nil then
            mode = tostring(world:getGameMode() or "")
        end
        if world.getMap ~= nil then
            map = tostring(world:getMap() or "")
        end
    end

    if mode == "" and map == "" then
        return "default"
    end

    return mode .. "::" .. map
end

local function currentSaveId()
    local value = ""
    local world = getWorld()
    if world ~= nil and world.getGameMode ~= nil then
        value = tostring(world:getGameMode() or "")
    end
    return value
end

local function playerName(player)
    if player == nil then
        return "Unknown"
    end

    local forename = ""
    if player.getForename ~= nil then
        forename = tostring(player:getForename() or "")
    elseif player.getForname ~= nil then
        forename = tostring(player:getForname() or "")
    elseif player.getDescriptor ~= nil then
        local descriptor = player:getDescriptor()
        if descriptor ~= nil then
            if descriptor.getForename ~= nil then
                forename = tostring(descriptor:getForename() or "")
            elseif descriptor.getForname ~= nil then
                forename = tostring(descriptor:getForname() or "")
            end
        end
    end

    local surname = ""
    if player.getSurname ~= nil then
        surname = tostring(player:getSurname() or "")
    elseif player.getDescriptor ~= nil then
        local descriptor = player:getDescriptor()
        if descriptor ~= nil and descriptor.getSurname ~= nil then
            surname = tostring(descriptor:getSurname() or "")
        end
    end

    local full = (forename .. " " .. surname):gsub("^%s+", ""):gsub("%s+$", "")
    if full ~= "" then
        return full
    end

    local username = ""
    if player.getUsername ~= nil then
        username = tostring(player:getUsername() or "")
    elseif player.getDisplayName ~= nil then
        username = tostring(player:getDisplayName() or "")
    end
    if username ~= "" then
        return username
    end

    return "Unknown"
end

local function addItemCount(target, fullType, count)
    if fullType == nil then
        return
    end

    local key = tostring(fullType)
    if key == "" then
        return
    end

    local n = tonumber(count) or 1
    if n < 1 then
        n = 1
    end

    target[key] = (target[key] or 0) + n
end

local function scanContainerItems(container, counts)
    if container == nil then
        return
    end

    local items = container:getItems()
    if items == nil then
        return
    end

    for i = 0, items:size() - 1 do
        local item = items:get(i)
        if item ~= nil then
            addItemCount(counts, item:getFullType(), item:getCount())
            if item:IsInventoryContainer() then
                local nested = item:getInventory()
                if nested ~= nil then
                    scanContainerItems(nested, counts)
                end
            end
        end
    end
end

local function scanPlayerInventory(player)
    local counts = {}
    if player == nil then
        return counts
    end

    local inventory = player:getInventory()
    if inventory ~= nil then
        scanContainerItems(inventory, counts)
    end

    return counts
end

local function scanBuilding(building, player)
    local itemCounts = {}
    local structureCounts = {}
    if building == nil or player == nil then
        return itemCounts, structureCounts
    end

    local square = player:getSquare()
    if square == nil then
        return itemCounts, structureCounts
    end

    local px = square:getX()
    local py = square:getY()
    local pz = square:getZ()
    local radius = 30
    local cell = getCell()

    for z = math.max(0, pz - 1), math.min(8, pz + 1) do
        for x = px - radius, px + radius do
            for y = py - radius, py + radius do
                local current = cell:getGridSquare(x, y, z)
                if current ~= nil and current:getBuilding() == building then
                    local objects = current:getObjects()
                    if objects ~= nil then
                        for oi = 0, objects:size() - 1 do
                            local obj = objects:get(oi)
                            if obj ~= nil then
                                local objectName = tostring(obj:getObjectName() or "")
                                if objectName ~= "" then
                                    structureCounts[objectName] = (structureCounts[objectName] or 0) + 1
                                end

                                local container = obj:getContainer()
                                if container ~= nil then
                                    scanContainerItems(container, itemCounts)
                                end
                            end
                        end
                    end
                end
            end
        end
    end

    return itemCounts, structureCounts
end

local function mapToItemArray(counts, defaultContainer)
    local result = {}
    for fullType, count in pairs(counts) do
        table.insert(result, {
            fullType = fullType,
            count = count,
            container = defaultContainer or "",
        })
    end
    return result
end

local function mapToStructureArray(counts, player)
    local result = {}
    local square = player ~= nil and player:getSquare() or nil
    local px = square ~= nil and square:getX() or 0
    local py = square ~= nil and square:getY() or 0
    local pz = square ~= nil and square:getZ() or 0
    for structureType, count in pairs(counts) do
        table.insert(result, {
            type = structureType,
            x = px,
            y = py,
            z = pz,
            count = count,
        })
    end
    return result
end

local function writeJsonToUrl(endpoint, payloadJson)
    if luajava == nil or luajava.bindClass == nil then
        return false, "luajava_unavailable"
    end

    local ok, errorText = pcall(function()
        local URL = luajava.bindClass("java.net.URL")
        local OutputStreamWriter = luajava.bindClass("java.io.OutputStreamWriter")

        local url = URL:new(endpoint)
        local connection = url:openConnection()
        connection:setRequestMethod("POST")
        connection:setDoOutput(true)
        connection:setConnectTimeout(300)
        connection:setReadTimeout(500)
        connection:setRequestProperty("Content-Type", "application/json; charset=utf-8")

        local writer = OutputStreamWriter:new(connection:getOutputStream(), "UTF-8")
        writer:write(payloadJson)
        writer:flush()
        writer:close()

        local code = connection:getResponseCode()
        if code >= 400 then
            error("http " .. tostring(code))
        end
    end)

    return ok, errorText
end

local function writeJsonToQueueFile(payloadJson)
    local ok, errorText = pcall(function()
        local writer = getFileWriter(ZGMB.QueueFilePath, true, true)
        if writer == nil then
            error("file_writer_unavailable")
        end

        writer:write(payloadJson)
        writer:write("\n")
        writer:close()
    end)

    return ok, errorText
end

local function sayTransportErrorThrottled(player, message)
    if player == nil then
        return
    end

    local now = nowEpoch()
    if now - ZGMB.LastTransportErrorEpoch < ZGMB.TransportErrorIntervalSeconds then
        return
    end

    ZGMB.LastTransportErrorEpoch = now
    player:Say(message)
end

local function encodePayload(payload)
    if JSON ~= nil and JSON.encode ~= nil then
        return JSON.encode(payload)
    end

    if json ~= nil and json.encode ~= nil then
        return json.encode(payload)
    end

    local function escapeJsonString(value)
        local text = tostring(value or "")
        text = text:gsub("\\", "\\\\")
        text = text:gsub("\"", "\\\"")
        text = text:gsub("\r", "\\r")
        text = text:gsub("\n", "\\n")
        text = text:gsub("\t", "\\t")
        return text
    end

    local function isArrayTable(value)
        if type(value) ~= "table" then
            return false
        end

        local maxIndex = 0
        local count = 0
        for key, _ in pairs(value) do
            if type(key) ~= "number" then
                return false
            end
            if key < 1 or key % 1 ~= 0 then
                return false
            end
            if key > maxIndex then
                maxIndex = key
            end
            count = count + 1
        end

        return maxIndex == count
    end

    local function encodeValue(value)
        local kind = type(value)
        if kind == "nil" then
            return "null"
        end
        if kind == "boolean" then
            return value and "true" or "false"
        end
        if kind == "number" then
            return tostring(value)
        end
        if kind == "string" then
            return "\"" .. escapeJsonString(value) .. "\""
        end
        if kind ~= "table" then
            return "\"" .. escapeJsonString(tostring(value)) .. "\""
        end

        if isArrayTable(value) then
            local items = {}
            for i = 1, #value do
                items[#items + 1] = encodeValue(value[i])
            end
            return "[" .. table.concat(items, ",") .. "]"
        end

        local keys = {}
        for key, _ in pairs(value) do
            keys[#keys + 1] = tostring(key)
        end
        table.sort(keys)

        local items = {}
        for _, key in ipairs(keys) do
            items[#items + 1] = "\"" .. escapeJsonString(key) .. "\":" .. encodeValue(value[key])
        end
        return "{" .. table.concat(items, ",") .. "}"
    end

    return encodeValue(payload)
end

local function sendSnapshot(baseEntry, player, building, playerInventoryCounts, baseItemCounts, structureCounts)
    local payload = {
        source = "zomboidguide-companion-mod",
        runKey = activeRunKey(),
        saveId = currentSaveId(),
        playerName = playerName(player),
        baseId = tostring(baseEntry.id or ""),
        baseName = tostring(baseEntry.name or ""),
        buildingId = getBuildingId(building),
        timestampUtc = os.date("!%Y-%m-%dT%H:%M:%SZ"),
        playerInventoryItems = mapToItemArray(playerInventoryCounts, "player"),
        baseItems = mapToItemArray(baseItemCounts, "world"),
        structures = mapToStructureArray(structureCounts, player),
    }

    local payloadJson = encodePayload(payload)
    local okHttp, errHttp = writeJsonToUrl(ZGMB.Endpoint, payloadJson)
    if okHttp then
        local now = nowEpoch()
        if now - ZGMB.LastHttpWriteLogEpoch >= 8 then
            ZGMB.LastHttpWriteLogEpoch = now
            print("[ZGMB] Snapshot sent via HTTP.")
        end
        return
    end

    local okQueue, errQueue = writeJsonToQueueFile(payloadJson)
    if okQueue then
        local now = nowEpoch()
        if now - ZGMB.LastQueueWriteLogEpoch >= 8 then
            ZGMB.LastQueueWriteLogEpoch = now
            print("[ZGMB] Snapshot queued to local file.")
        end
        return
    end

    sayTransportErrorThrottled(
        player,
        "[ZG] Snapshot send failed. HTTP=" .. tostring(errHttp) .. " FILE=" .. tostring(errQueue))
end

local function addOrRenameBaseCurrentBuilding()
    local player = playerOrNil()
    if player == nil then
        return
    end

    local building = buildingForPlayer(player)
    if building == nil then
        player:Say("[ZG] No building at current position.")
        return
    end

    local state = getState()
    local id = getBuildingId(building)
    if id == "" then
        player:Say("[ZG] Could not resolve building id.")
        return
    end

    local existing = state.bases[id]
    local baseCounter = 1
    for _, _ in pairs(state.bases) do
        baseCounter = baseCounter + 1
    end
    local defaultName = existing and existing.name or ("Base " .. tostring(baseCounter))

    local function saveBase(baseName)
        state.bases[id] = {
            id = id,
            name = baseName,
        }
        ModData.transmit(ZGMB.ModDataKey)
        player:Say("[ZG] Base saved: " .. tostring(baseName))
    end

    if not ensureTextBoxClass() then
        saveBase(defaultName)
        print("[ZGMB] ISTextBox unavailable, base saved without popup.")
        return
    end

    local width = 420
    local height = 180
    local modal = ISTextBox:new(
        (getCore():getScreenWidth() / 2) - (width / 2),
        (getCore():getScreenHeight() / 2) - (height / 2),
        width,
        height,
        "ZomboidGuide Base Name",
        defaultName,
        nil,
        function(_, button)
            if button.internal ~= "OK" then
                return
            end

            local raw = tostring(button.parent.entry:getText() or "")
            local clean = raw:gsub("^%s+", ""):gsub("%s+$", "")
            if clean == "" then
                clean = defaultName
            end

            saveBase(clean)
        end
    )
    modal:initialise()
    modal:addToUIManager()
end

local function removeCurrentBuildingBase()
    local player = playerOrNil()
    if player == nil then
        return
    end

    local building = buildingForPlayer(player)
    if building == nil then
        player:Say("[ZG] No building at current position.")
        return
    end

    local id = getBuildingId(building)
    if id == "" then
        return
    end

    local state = getState()
    if state.bases[id] == nil then
        player:Say("[ZG] Current building is not tracked.")
        return
    end

    state.bases[id] = nil
    ModData.transmit(ZGMB.ModDataKey)
    player:Say("[ZG] Base removed.")
end

local function scanTrackedBases()
    local player = playerOrNil()
    if player == nil then
        ZGMB.WasF8Down = false
        ZGMB.WasF7Down = false
        local noPlayerNow = nowEpoch()
        if noPlayerNow - ZGMB.LastNoPlayerLogEpoch >= 12 then
            ZGMB.LastNoPlayerLogEpoch = noPlayerNow
            print("[ZGMB] Scan loop active, but no player object resolved.")
        end
        return
    end

    local heartbeatNow = nowEpoch()
    if heartbeatNow - ZGMB.LastScanHeartbeatEpoch >= 12 then
        ZGMB.LastScanHeartbeatEpoch = heartbeatNow
        print("[ZGMB] Scan loop active.")
    end

    local now = nowEpoch()
    if now - ZGMB.LastScanEpoch < ZGMB.ScanIntervalSeconds then
        return
    end
    ZGMB.LastScanEpoch = now

    local state = getState()
    if state.bases == nil then
        return
    end

    local playerInventoryCounts = scanPlayerInventory(player)
    for id, base in pairs(state.bases) do
        local baseBuilding = nil
        local currentBuilding = buildingForPlayer(player)
        if currentBuilding ~= nil and getBuildingId(currentBuilding) == id then
            baseBuilding = currentBuilding
        end

        local baseItemCounts = {}
        local structureCounts = {}
        if baseBuilding ~= nil then
            baseItemCounts, structureCounts = scanBuilding(baseBuilding, player)
        end

        sendSnapshot(base, player, baseBuilding, playerInventoryCounts, baseItemCounts, structureCounts)
    end
end

local function resolveKeyFromArgs(...)
    local argc = select("#", ...)
    if argc <= 0 then
        return nil
    end

    for i = argc, 1, -1 do
        local value = select(i, ...)
        if type(value) == "number" then
            return math.floor(value)
        end
    end

    return nil
end

local function handleHotkeyAction(key, action)
    local now = nowEpoch()
    if key == ZGMB.LastHandledHotkey and now == ZGMB.LastHandledHotkeyEpoch then
        return
    end

    ZGMB.LastHandledHotkey = key
    ZGMB.LastHandledHotkeyEpoch = now

    local ok, err = pcall(action)
    if not ok then
        print("[ZGMB] Hotkey action failed: " .. tostring(err))
    end
end

local function resolveHotkeyCodes()
    local f8 = 66
    local f7 = 65
    if Keyboard ~= nil then
        if Keyboard.KEY_F8 ~= nil then
            f8 = Keyboard.KEY_F8
        end
        if Keyboard.KEY_F7 ~= nil then
            f7 = Keyboard.KEY_F7
        end
    end
    return f8, f7
end

local function isKeyCurrentlyDown(keyCode)
    if keyCode == nil then
        return false
    end

    if Keyboard ~= nil and Keyboard.isKeyDown ~= nil then
        local ok, down = pcall(function()
            return Keyboard.isKeyDown(keyCode)
        end)
        if ok then
            return down
        end
    end

    if isKeyDown ~= nil then
        local ok, down = pcall(function()
            return isKeyDown(keyCode)
        end)
        if ok then
            return down
        end
    end

    return false
end

pollHotkeysByState = function()
    local f8, f7 = resolveHotkeyCodes()

    local f8Down = isKeyCurrentlyDown(f8) or isKeyCurrentlyDown(66)
    ZGMB.WasF8Down = f8Down

    local f7Down = isKeyCurrentlyDown(f7) or isKeyCurrentlyDown(65)
    ZGMB.WasF7Down = f7Down
end

local function onKeyPressed(...)
    local key = resolveKeyFromArgs(...)
    if key == nil then
        return
    end

    local traceNow = nowEpoch()
    if traceNow - ZGMB.LastKeyTraceEpoch >= 8 then
        ZGMB.LastKeyTraceEpoch = traceNow
        print("[ZGMB] Key event captured: " .. tostring(key))
    end

    local f8, f7 = resolveHotkeyCodes()

    if key == f8 or key == 66 then
        print("[ZGMB] Hotkey Add/Rename Base pressed.")
        handleHotkeyAction(key, addOrRenameBaseCurrentBuilding)
        return
    end

    if key == f7 or key == 65 then
        print("[ZGMB] Hotkey Remove Base pressed.")
        handleHotkeyAction(key, removeCurrentBuildingBase)
    end
end

local function safeAddEvent(eventName, handler)
    if Events == nil then
        print("[ZGMB] Events table is unavailable.")
        return false
    end

    local added = false
    local ok, err = pcall(function()
        local evt = Events[eventName]
        if evt == nil then
            return
        end

        local addFunction = evt.Add or evt.add
        if addFunction == nil then
            error("missing Add")
        end

        local calledDirect = pcall(function()
            addFunction(handler)
        end)

        if calledDirect then
            added = true
            return
        end

        local calledWithColon = pcall(function()
            evt:Add(handler)
        end)
        if calledWithColon then
            added = true
            return
        end

        local calledWithSelf = pcall(function()
            addFunction(evt, handler)
        end)
        if calledWithSelf then
            added = true
            return
        end

        error("add_call_failed")
    end)
    if ok and added then
        print("[ZGMB] Registered event: " .. tostring(eventName))
        return true
    end

    if ok then
        print("[ZGMB] Event unavailable: " .. tostring(eventName))
    else
        print("[ZGMB] Event registration failed for " .. tostring(eventName) .. ": " .. tostring(err))
    end
    return false
end

local scanEverySecondHooked = false
local scanTickHooked = false
local scanPlayerUpdateHooked = false
local keyPressedHooked = false
local keyStartHooked = false

local function registerHandlers()
    if not scanEverySecondHooked then
        scanEverySecondHooked = safeAddEvent("EveryOneSecond", scanTrackedBases)
    end

    if not scanTickHooked then
        scanTickHooked = safeAddEvent("OnTick", scanTrackedBases)
    end

    if not scanPlayerUpdateHooked then
        scanPlayerUpdateHooked = safeAddEvent("OnPlayerUpdate", scanTrackedBases)
    end

    if not keyPressedHooked then
        keyPressedHooked = safeAddEvent("OnKeyPressed", onKeyPressed)
    end

    if not keyStartHooked then
        keyStartHooked = safeAddEvent("OnKeyStartPressed", onKeyPressed)
    end
end

registerHandlers()
safeAddEvent("OnGameStart", registerHandlers)
