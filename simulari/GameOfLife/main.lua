-- CHECK IF IN WORLD
function isInWorld(x, y, world)
    if x <= #world - 1 and x > 0 then
        if y <= #world[1] - 1 and y > 0 then
            return true
        end
    end
    return false
end

-- CHECK CELL
function checkCell(x, y, world)
    if (not isInWorld(x, y, world)) then
        return false
    end

    local count = 0

    -- cound neighbour cells
    count = count +
        --top
        world[x - 1][y - 1].current +
        world[x + 0][y - 1].current +
        world[x + 1][y - 1].current +

        --middle
        world[x - 1][y].current +
        --[center]
        world[x + 1][y].current +

        --bottom
        world[x - 1][y + 1].current +
        world[x + 0][y + 1].current +
        world[x + 1][y + 1].current

    -- apply rules
    if (count < 2 or count > 3) then
        world[x][y].future = 0
        return
    end
    if (count == 3) then
        world[x][y].future = 1
        return
    end
    world[x][y].color = world[x][y].color + 1 * world[x][y].current
    world[x][y].future = world[x][y].current
end

-- UPDATE
function update(world)
    for x = 1, xSize do
        for y = 1, ySize do
            -- check cells
            checkCell(x, y, world)
        end
    end
    for x = 1, xSize do
        for y = 1, ySize do
            -- update cells
            world[x][y].current = world[x][y].future
            world[x][y].future = 0
        end
    end
end

-- RENDER
function render(world)
    for x = 1, xSize do
        for y = 1, ySize do
            -- draw cells
            if (world[x][y].current == 1) then
                xRatio = love.graphics.getWidth() / xSize
                yRatio = love.graphics.getHeight() / ySize
                love.graphics.setColor(1, 1, 1, world[x][y].color)
                love.graphics.rectangle(
                    "fill",
                    x * xRatio, y * yRatio,
                    xRatio, yRatio
                )
                love.graphics.setColor(1, 1, 1, 0)
                love.graphics.rectangle(
                    "line",
                    x * xRatio, y * yRatio,
                    xRatio, yRatio
                )
            end
        end
    end
end

-- DRAW
function drawOnWorld(x, y, world)
    xRatio = love.graphics.getWidth() / xSize
    yRatio = love.graphics.getHeight() / ySize

    x = math.floor(x / xRatio)
    y = math.floor(y / yRatio)

    if (not isInWorld(x, y, world)) then
        return false
    end

    world[x][y].current = 1
end

-- GENERATE WORLD
function newWorld(xSize, ySize)
    local world = {}
    for x = 0, xSize + 1 do
        world[x] = {}
        for y = 0, ySize + 1 do
            world[x][y] = {
                future = 0,
                current = 0,
                color = 0
            }
        end
    end
    return world
end

-------------------------------------
---SETUP
xSize = 100
ySize = 100

world = newWorld(xSize, ySize)

-------------------------------------
---ACTION

function love.draw()
    if love.mouse.isDown(1) then
        drawOnWorld(
            love.mouse.getX(),
            love.mouse.getY(),
            world
        )
    end
    if (love.keyboard.isDown("w") == true) then
        update(world)
    end
    render(world)
end
