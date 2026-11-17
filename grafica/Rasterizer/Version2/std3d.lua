std3d = {}

function std3d.canvas_init(z_range)
    std3d.z_buffer = {}
    for x = 1, width do
        std3d.z_buffer[x] = {}
        for y = 1, height do
            std3d.z_buffer[x][y] = {}
            std3d.z_buffer[x][y].color = { 0, 0, 0 }
            std3d.z_buffer[x][y].z = z_range
        end
    end

    function std3d.canvas_draw()
        for x = 1, width do
            for y = 1, height do
                love.graphics.setColor(
                    std3d.z_buffer[x][y].color[1],
                    std3d.z_buffer[x][y].color[2],
                    std3d.z_buffer[x][y].color[3]
                )
                love.graphics.points(x - 1, y - 1)
            end
        end
    end
end

-- love.graphics.print("Buffer size check:" .. #std3d.z_buffer .. width)
-- love.graphics.present()
-- while true do

-- end
function std3d.point(x, y, z, color)
    if x >= 0 and x < width and y >= 0 and y < height then
        std3d.z_buffer[math.floor(x + 1)][math.floor(y + 1)] = {
            z = z,
            color = color
        }
    end
end

function std3d.triangle(points)
    --points = {{x, y, color}, {x, y, color}, {x, y, color}}

    table.sort(
        points,
        function(a, b)
            return a.y < b.y
        end
    )
    --points[1] = top
    --points[2] = mid
    --points[3] = bot

    local y_mid = points[2].y - points[1].y
    local y_tot = points[3].y - points[1].y
    local y_bot = y_tot - y_mid
    --Top half
    for i = 0, y_mid do
        local t1 = i / y_mid
        local t3 = i / y_tot

        local s1 = t1 * points[1].z / ((1 - t1) * points[3].z + t1 * points[1].z)
        local s3 = t3 * points[1].z / ((1 - t3) * points[2].z + t3 * points[1].z)

        local current_x1 = math.floor(points[1].x * (1 - t3) + points[3].x * t3)
        local current_x2 = math.floor(points[1].x * (1 - t1) + points[2].x * t1)

        local current_z1 = points[1].z * (1 - t3) + points[3].z * t3
        local current_z2 = points[1].z * (1 - t1) + points[2].z * t1

        local current_y = points[1].y + i

        local color1 = {
            points[1].color[1] * (1 - s3) + points[3].color[1] * s3,
            points[1].color[2] * (1 - s3) + points[3].color[2] * s3,
            points[1].color[3] * (1 - s3) + points[3].color[3] * s3
        }
        local color2 = {
            points[1].color[1] * (1 - s1) + points[2].color[1] * s1,
            points[1].color[2] * (1 - s1) + points[2].color[2] * s1,
            points[1].color[3] * (1 - s1) + points[2].color[3] * s1
        }
        for j = 0, math.abs(current_x2 - current_x1) do
            local t2 = j / math.abs(current_x2 - current_x1)
            local s2 = t2 * current_z1 / ((1 - t2) * current_z2 + t2 * current_z1)
            std3d.point(
                current_x2 - current_x1 >= 0 and current_x1 + j or current_x1 - j,
                current_y,
                current_z2 * t2 + current_z1 * (1 - t2),
                {
                    color1[1] * (1 - s2) + color2[1] * s2,
                    color1[2] * (1 - s2) + color2[2] * s2,
                    color1[3] * (1 - s2) + color2[3] * s2
                }
            )
        end
    end

    --Bottom half
    for i = 0, y_bot do
        local t1 = i / y_bot
        local t3 = (i + y_mid) / y_tot

        local s1 = t1 * points[1].z / ((1 - t1) * points[3].z + t1 * points[1].z)
        local s3 = t3 * points[2].z / ((1 - t3) * points[3].z + t3 * points[2].z)

        local current_x1 = math.floor(points[1].x * (1 - t3) + points[3].x * t3)
        local current_x2 = math.floor(points[2].x * (1 - t1) + points[3].x * t1)

        local current_z1 = points[1].z * (1 - t3) + points[3].z * t3
        local current_z2 = points[2].z * (1 - t1) + points[3].z * t1

        local current_y = y_mid + points[1].y + i

        local color1 = {
            points[1].color[1] * (1 - s3) + points[3].color[1] * s3,
            points[1].color[2] * (1 - s3) + points[3].color[2] * s3,
            points[1].color[3] * (1 - s3) + points[3].color[3] * s3
        }
        local color2 = {
            points[2].color[1] * (1 - s1) + points[3].color[1] * s1,
            points[2].color[2] * (1 - s1) + points[3].color[2] * s1,
            points[2].color[3] * (1 - s1) + points[3].color[3] * s1
        }
        for j = 0, math.abs(current_x2 - current_x1) do
            local t2 = j / math.abs(current_x2 - current_x1)
            local s2 = t2 * current_z1 / ((1 - t2) * current_z2 + t2 * current_z1)
            std3d.point(
                current_x2 - current_x1 >= 0 and current_x1 + j or current_x1 - j,
                current_y,
                current_z2 * t2 + current_z1 * (1 - t2),
                {
                    color1[1] * (1 - s2) + color2[1] * s2,
                    color1[2] * (1 - s2) + color2[2] * s2,
                    color1[3] * (1 - s2) + color2[3] * s2
                }
            )
        end
    end
end
