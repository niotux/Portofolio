std3d = {}

function std3d.canvas_init(z_range)
    std3d.canvas = {}
    for x = 1, love.graphics.getWidth() do
        std3d.canvas[x] = {}
        for y = 1, love.graphics.getHeight() do
            std3d.canvas[x][y] = {
                z = z_range,
                color = { 0, 0, 0 }
            }
        end
    end
end

function std3d.canvas_clear(z_range)
    for x = 1, love.graphics.getWidth() do
        for y = 1, love.graphics.getHeight() do
            std3d.canvas[x][y] = {
                z = z_range,
                color = { 0, 0, 0 }
            }
        end
    end
end

function std3d.canvas_draw()
    for x = 1, love.graphics.getWidth() do
        for y = 1, love.graphics.getHeight() do
            love.graphics.setColor(std3d.canvas[x][y].color)
            love.graphics.points(x, y)
        end
    end
end

function std3d.plot(x, y, z, color)
    if x >= 0 and x <= 999 and y >= 0 and y <= 999 then
        if z <= std3d.canvas[x + 1][y + 1].z then
            std3d.canvas[x + 1][y + 1] = {
                color = { color[1], color[2], color[3] },
                z = z
            }
        end
    end
end

function std3d.triangle(points)
    table.sort(
        points,
        function(a, b)
            return a.y < b.y
        end
    )
    --points[1] - top
    --points[2] - middle
    --points[3] - bottom
    local top_half = points[2].y - points[1].y
    local bot_half = points[3].y - points[2].y
    local total = points[3].y - points[1].y

    --TOP HALF
    for current_y = 0, top_half - 1 do
        local t1 = current_y / total    --factor for edge top-bot
        local t2 = current_y / top_half --factor for edge top-mid
        local s1 = t1 * points[1].z / ((1 - t1) * points[3].z + t1 * points[1].z)
        local s2 = t2 * points[1].z / ((1 - t2) * points[2].z + t2 * points[1].z)
        --current X
        local current_x1 = math.ceil(points[1].x + t1 * (points[3].x - points[1].x))
        local current_x2 = math.ceil(points[1].x + t2 * (points[2].x - points[1].x))
        --current Z
        local current_z1 = points[1].z + t1 * (points[3].z - points[1].z)
        local current_z2 = points[1].z + t2 * (points[2].z - points[1].z)
        --curent COLOR
        local current_color1 = {
            points[1].color[1] + s1 * (points[3].color[1] - points[1].color[1]),
            points[1].color[2] + s1 * (points[3].color[2] - points[1].color[2]),
            points[1].color[3] + s1 * (points[3].color[3] - points[1].color[3])
        }
        local current_color2 = {
            points[1].color[1] + s2 * (points[2].color[1] - points[1].color[1]),
            points[1].color[2] + s2 * (points[2].color[2] - points[1].color[2]),
            points[1].color[3] + s2 * (points[2].color[3] - points[1].color[3])
        }
        --scanlines
        for scan = 0, math.abs(current_x2 - current_x1) do
            local t_scan = scan / math.abs(current_x2 - current_x1)
            local s_scan = t_scan * current_z1 / ((1 - t_scan) * current_z2 + t_scan * current_z1)
            std3d.plot(
                current_x2 - current_x1 >= 0 and current_x1 + scan or current_x1 - scan,
                current_y,
                current_z1 + t_scan * (current_z2 - current_z1),
                {
                    current_color1[1] + t_scan * (current_color2[1] - current_color1[1]),
                    current_color1[2] + t_scan * (current_color2[2] - current_color1[2]),
                    current_color1[3] + t_scan * (current_color2[3] - current_color1[3])
                }
            )
        end
        --love.graphics.points(current_x1, current_y)
    end

    --BOTTOM HALF
    for current_y = 0, bot_half do
        local t1 = (current_y + top_half) / total --factor for edge top-bot
        local t2 = current_y / bot_half           --factor for edge mid-bot
        local s1 = t1 * points[1].z / ((1 - t1) * points[3].z + t1 * points[1].z)
        local s2 = t2 * points[2].z / ((1 - t2) * points[3].z + t2 * points[2].z)
        --current X
        local current_x1 = math.ceil(points[1].x + t1 * (points[3].x - points[1].x))
        local current_x2 = math.ceil(points[2].x + t2 * (points[3].x - points[2].x))
        --current Z
        local current_z1 = points[1].z + t1 * (points[3].z - points[1].z)
        local current_z2 = points[2].z + t2 * (points[3].z - points[2].z)
        --curent COLOR
        local current_color1 = {
            points[1].color[1] + s1 * (points[3].color[1] - points[1].color[1]),
            points[1].color[2] + s1 * (points[3].color[2] - points[1].color[2]),
            points[1].color[3] + s1 * (points[3].color[3] - points[1].color[3])
        }
        local current_color2 = {
            points[2].color[1] + s2 * (points[3].color[1] - points[2].color[1]),
            points[2].color[2] + s2 * (points[3].color[2] - points[2].color[2]),
            points[2].color[3] + s2 * (points[3].color[3] - points[2].color[3])
        }
        --scanlines
        for scan = 0, math.abs(current_x2 - current_x1) do
            local t_scan = scan / math.abs(current_x2 - current_x1)
            local s_scan = t_scan * current_z1 / ((1 - t_scan) * current_z2 + t_scan * current_z1)
            std3d.plot(
                current_x2 - current_x1 >= 0 and current_x1 + scan or current_x1 - scan,
                current_y + top_half,
                current_z1 + t_scan * (current_z2 - current_z1),
                {
                    current_color1[1] + t_scan * (current_color2[1] - current_color1[1]),
                    current_color1[2] + t_scan * (current_color2[2] - current_color1[2]),
                    current_color1[3] + t_scan * (current_color2[3] - current_color1[3])
                }
            )
        end
        --love.graphics.points(current_x1, current_y)
    end
end
