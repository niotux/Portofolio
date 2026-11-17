cursor = love.graphics.newImage("textures/cursor.png")
love.mouse.setVisible(not love.mouse.isVisible())

size = 11 --World size
scale = 1 --Map scale (for zoom-in)

fac_nbr = 0
vtx_nbr = 0
vtx_grid = {}        --The grid of vertices (state: 0 -- n/a| 1 -- existent)
selected_vtx = {}    --Vertices selected previously ({xmap_1, ymap_1}, {xmap_2, ymap_2} )
selected_vtx[1] = {} --{xmap_1, ymap_1}
selected_vtx[2] = {} --{xmap_2, ymap_2}
vtx_result = {}      --Faces ({{x = xmap_1, y = ymap_1} -- vtx, {x = xmap_2, y = ymap_2} -- vtx_linked})

--vtx_grid init
for y = 1, size do
    vtx_grid[y] = {}
    for x = 1, size do
        vtx_grid[y][x] = 0
    end
end

--Function that draws the grid
function grid()
    for y = 0, size do
        for x = 0, size - 1 do
            love.graphics.points(x * unit + width / 2 - (size - 1) * unit / 2, y * unit - unit / 2)
        end
    end
end

--ACTIVE PART
--LOVE.Update---------------------------------------------------------------------------
function love.update(dt)
    width = love.graphics.getWidth()
    height = love.graphics.getHeight()
    xpos = love.mouse.getX()                                     -- X mouse position
    ypos = love.mouse.getY()                                     -- Y mouse position
    unit = height / size * scale
    xmap = math.floor((xpos - width / 2) / unit + size / 2 + 1)  --in map coordonate
    ymap = math.floor((ypos - height / 2) / unit + size / 2 + 1) --in map coordonate
    if love.keyboard.isDown("f") and selected_vtx[1].x ~= nil and selected_vtx[2].x ~= nil then
        fac_nbr = fac_nbr + 1
        vtx_result[fac_nbr] = {}
        vtx_result[fac_nbr][1] = {}
        vtx_result[fac_nbr][2] = {}
        vtx_result[fac_nbr][1].x = selected_vtx[1].x
        vtx_result[fac_nbr][1].y = selected_vtx[1].y
        vtx_result[fac_nbr][2].x = selected_vtx[2].x
        vtx_result[fac_nbr][2].y = selected_vtx[2].y
    end
end

--LOVE.Draw---------------------------------------------------------------------------
function love.draw()
    --Vertex box selection cursor
    if size / 2 == math.floor(size / 2) then
        love.graphics.rectangle("line",
            width / 2 + math.floor((xpos - width / 2 + unit) / unit) * unit - unit,
            height / 2 + math.floor((ypos - height / 2 + unit) / unit) * unit - unit,
            unit, unit
        )
    else
        love.graphics.rectangle("line",
            width / 2 + math.floor((xpos - width / 2 + unit / 2) / unit) * unit - unit / 2,
            height / 2 + math.floor((ypos - height / 2 + unit / 2) / unit) * unit - unit / 2,
            unit, unit
        )
    end

    --Draw grid
    grid()

    --Draw vertices on grid (white)
    for y = 1, size do
        for x = 1, size do
            if vtx_grid[y][x] == 1 then
                love.graphics.circle("fill",
                    (x - 1) * unit + width / 2 - (size - 1) * unit / 2,
                    y * unit - unit / 2,
                    unit / 20
                )
            end
        end
    end

    --Draw faces
    for i = 1, fac_nbr do
        love.graphics.line(
            (vtx_result[i][1].x - 1) * unit + width / 2 - (size - 1) * unit / 2,
            vtx_result[i][1].y * unit - unit / 2,
            (vtx_result[i][2].x - 1) * unit + width / 2 - (size - 1) * unit / 2,
            vtx_result[i][2].y * unit - unit / 2)
    end

    --Draw selected vertices (green)

    --draws selected vertex 1
    if selected_vtx[1].x ~= nil and selected_vtx[1].y ~= nil then
        love.graphics.setColor(0, 1, 0, 1)
        love.graphics.circle("fill",
            (selected_vtx[1].x - 1) * unit + width / 2 - (size - 1) * unit / 2,
            selected_vtx[1].y * unit - unit / 2,
            unit / 20
        )
        love.graphics.circle("line",
            (selected_vtx[1].x - 1) * unit + width / 2 - (size - 1) * unit / 2,
            selected_vtx[1].y * unit - unit / 2,
            unit / 10
        )
        love.graphics.setColor(1, 1, 1, 1)
    end
    --draws selected vertex 2
    if selected_vtx[2].x ~= nil and selected_vtx[2].y ~= nil then
        love.graphics.setColor(0, 1, 0, 1)
        love.graphics.circle("fill",
            (selected_vtx[2].x - 1) * unit + width / 2 - (size - 1) * unit / 2,
            selected_vtx[2].y * unit - unit / 2,
            unit / 20
        )
        love.graphics.setColor(1, 1, 1, 1)
    end

    --Print mqp position of cursor
    love.graphics.print(xmap, 10, 10)
    love.graphics.print(ymap, 10, 40)

    --Draw cursor
    if xpos > 0 and xpos < width - 1 and ypos > 0 and ypos < height - 1 then
        love.graphics.draw(cursor, xpos, ypos)
    end
end

--LOVE.mousereleased---------------------------------------------------------------------------
function love.mousereleased(xx, yy, mouse_button)
    --Left click actions
    if mouse_button == 1 then
        if vtx_grid[ymap][xmap] == 0 then
            vtx_grid[ymap][xmap] = 1
        else
            vtx_grid[ymap][xmap] = 0
        end
        if selected_vtx[1].x ~= nil and selected_vtx[1].y ~= nil then
            if vtx_grid[selected_vtx[1].y][selected_vtx[1].x] == 0 then
                selected_vtx[1].x = selected_vtx[2].x
                selected_vtx[1].y = selected_vtx[2].y
                selected_vtx[2].x = nil
                selected_vtx[2].y = nil
            end

            if selected_vtx[2].x ~= nil and selected_vtx[2].y ~= nil then
                if vtx_grid[selected_vtx[2].y][selected_vtx[2].x] == 0 then
                    selected_vtx[2].x = nil
                    selected_vtx[2].y = nil
                end
            end
        end
    else
        --Right click actions
        if mouse_button == 2 then
            if selected_vtx[1].x == xmap and selected_vtx[1].y == ymap then
                selected_vtx[1].x = selected_vtx[2].x
                selected_vtx[1].y = selected_vtx[2].y
                selected_vtx[2].x = nil
                selected_vtx[2].y = nil
            else
                if selected_vtx[2].x == xmap and selected_vtx[2].y == ymap then
                    selected_vtx[2].x = nil
                    selected_vtx[2].y = nil
                else
                    if vtx_grid[ymap][xmap] == 1 then
                        selected_vtx[2] = selected_vtx[1]
                        selected_vtx[1] = { x = xmap, y = ymap }
                    end
                end
            end
        end
    end
end

-- function love.quit()
--     file:write("}")
-- end
