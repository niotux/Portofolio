std2d = {}

--Line drawing (under developement)
function std2d.line(points)
    local Xlength = points[2].x - points[1].x
    local Ylength = points[2].y - points[1].y
    for i = 0, math.abs(Xlength) do
        local t = i / math.abs(Xlength) --interpolation value (0 <= rap <= 1)

        --Line from Point_1 to Point_2
        love.graphics.setColor(
            points[2].color.r * t + points[1].color.r * (1 - t),
            points[2].color.g * t + points[1].color.g * (1 - t),
            points[2].color.b * t + points[1].color.b * (1 - t)
        )
        love.graphics.points(
            points[1].x + Xlength * t,
            points[1].y + Ylength * t
        )
    end
    love.graphics.setColor(1, 1, 1, 1)
end

--Horizontal line
function std2d.line_h(points)
    local Xlength = points.x2 - points.x1
    for i = 0, math.abs(Xlength) do
        local t = i / math.abs(Xlength) --interpolation value (0 <= rap <= 1)

        --Line from Point_1 to Point_2
        love.graphics.setColor(
            points.color2.r * t + points.color1.r * (1 - t),
            points.color2.g * t + points.color1.g * (1 - t),
            points.color2.b * t + points.color1.b * (1 - t)
        )
        if Xlength >= 0 then
            love.graphics.points(
                points.x1 + i,
                points.y
            )
        else
            love.graphics.points(
                points.x1 - i,
                points.y
            )
        end
    end
    love.graphics.setColor(1, 1, 1, 1)
end

--Line data calculations
function std2d.line_data(points)
    local rows = {}
    local Xlength = points[2].x - points[1].x
    local Ylength = points[2].y - points[1].y
    for i = 1, math.abs(Ylength) + 1 do
        local t = (i - 1) / math.abs(Ylength) --interpolation value (0 <= rap <= 1)

        --rows storing
        rows[i] = {
            x = points[1].x + math.floor(Xlength * t),
            color = {
                r = points[2].color.r * t + points[1].color.r * (1 - t),
                g = points[2].color.g * t + points[1].color.g * (1 - t),
                b = points[2].color.b * t + points[1].color.b * (1 - t)
            }
        }
        if Ylength >= 0 then
            rows[i].y = points[1].y + i
        else
            rows[i].y = points[1].y - i
        end
    end
    return rows
end

--Triangle function
function std2d.triangle(points)
    --sorting the points top to bottom
    table.sort(points, function(a, b) return a.y < b.y end)
    local edge1 = std2d.line_data({ points[1], points[2] }) --top to mid edge
    local edge2 = std2d.line_data({ points[2], points[3] }) --mid to bottom edge
    local edge3 = std2d.line_data({ points[1], points[3] }) --top to bottom edge (the longest edge)

    --draws the top part of the triangle
    local y_mid = math.floor(points[2].y - points[1].y)

    for i = 1, y_mid do
        --draws each horizontal line with color interpolation
        std2d.line_h({
            y = i - 1 + points[1].y,
            x1 = edge1[i].x,
            x2 = edge3[i].x,
            color1 = edge1[i].color,
            color2 = edge3[i].color
        })
    end
    for i = y_mid + 1, points[3].y - points[1].y do
        --draws each horizontal line with color interpolation
        std2d.line_h({
            y = i - 1 + points[1].y,
            x2 = edge2[i - y_mid].x,
            x1 = edge3[i].x,
            color2 = edge2[i - y_mid].color,
            color1 = edge3[i].color
        })
    end
end
