std3d = {}

std3d.canvas = {} --canvas table
--Reset_canvas function
function std3d.reset_canvas(z_range)
    for y = 1, love.graphics.getHeight() + 1 do
        std3d.canvas[y] = {}
        for x = 1, love.graphics.getWidth() + 1 do
            std3d.canvas[y][x] = { z = z_range, color = { r = 0, g = 0, b = 0 } }
        end
    end
end

--Draw_canvas function
function std3d.draw_canvas()
    for y = 1, love.graphics.getHeight() + 1 do
        for x = 1, love.graphics.getWidth() + 1 do
            love.graphics.setColor(
                std3d.canvas[y][x].color.r,
                std3d.canvas[y][x].color.g,
                std3d.canvas[y][x].color.b
            )
            love.graphics.points(x - 1, y - 1)
        end
    end
    love.graphics.setColor(1, 1, 1, 1)
end

--Horizontal line (rasterizer)
function std3d.scanline_draw(points)
    local Xlength = points.x2 - points.x1
    for i = 0, math.abs(Xlength) do
        local t = i / math.abs(Xlength) --interpolation value (0 <= rap <= 1)
        local s = t * points.x1 / ((1 - t) * points.x2 + t * points.x1)
        local current_z = math.floor(points.z2 * t + points.z1 * (1 - t))
        local current_x = Xlength >= 0 and points.x1 + i or points.x1 - i


        --Line from Point_1 to Point_2 with depth test
        if points.y >= 0 and points.y < height and points.x1 >= 0 and points.x1 < width and points.x2 >= 0 and points.x2 < width then
            if std3d.canvas[points.y + 1][current_x + 1].z > current_z then
                std3d.canvas[points.y + 1][current_x + 1] = {
                    color = {
                        r = points.color2.r * s + points.color1.r * (1 - s),
                        g = points.color2.g * s + points.color1.g * (1 - s),
                        b = points.color2.b * s + points.color1.b * (1 - s)
                    },
                    z = current_z
                }
            end
        end
    end
    love.graphics.setColor(1, 1, 1, 1)
end

--Line data calculations (rasterizer)
function std3d.line_data(points)
    local rows = {}
    local Xlength = points[2].x - points[1].x
    local Ylength = points[2].y - points[1].y
    for i = 1, math.abs(Ylength) + 1 do
        local t = (i - 1) / math.abs(Ylength) --interpolation value (0 <= rap <= 1)
        local s = t * points[1].z / ((1 - t) * points[2].z + t * points[1].z)
        local current_z = math.floor(points[1].z + t * (points[2].z - points[1].z))

        --rows storing
        rows[i] = {
            x = math.floor(points[1].x + Xlength * t),
            z = current_z,
            color = {
                r = points[2].color.r * s + points[1].color.r * (1 - s),
                g = points[2].color.g * s + points[1].color.g * (1 - s),
                b = points[2].color.b * s + points[1].color.b * (1 - s)
            }
        }
        rows[i].y = Ylength >= 0 and points[1].y + i or points[1].y - i
    end
    return rows
end

--Triangle function (rasterizer)
function std3d.triangle(points)
    --sorting the points top to bottom
    table.sort(points, function(a, b) return a.y < b.y end)
    local edge1 = std3d.line_data({ points[1], points[2] }) --top to mid edge
    local edge2 = std3d.line_data({ points[2], points[3] }) --mid to bottom edge
    local edge3 = std3d.line_data({ points[1], points[3] }) --top to bottom edge (the longest edge)

    --draws the top part of the triangle
    local y_mid = math.floor(points[2].y - points[1].y)

    for i = 1, y_mid do
        --draws each horizontal line with color interpolation
        std3d.scanline_draw({
            y = i - 1 + points[1].y,
            x1 = edge1[i].x,
            x2 = edge3[i].x,
            z1 = edge1[i].z,
            z2 = edge3[i].z,
            color1 = edge1[i].color,
            color2 = edge3[i].color
        })
    end
    for i = y_mid + 1, points[3].y - points[1].y do
        --draws each horizontal line with color interpolation
        std3d.scanline_draw({
            y = i - 1 + points[1].y,
            x2 = edge2[i - y_mid].x,
            x1 = edge3[i].x,
            z2 = edge2[i - y_mid].z,
            z1 = edge3[i].z,
            color2 = edge2[i - y_mid].color,
            color1 = edge3[i].color
        })
    end
end
