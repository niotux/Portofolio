require("std2d")
require("std3d")

color1 = { r = 0, g = 1, b = 0 }
color2 = { r = 1, g = 1, b = 0 }
color3 = { r = 1, g = 0.5, b = 0 }
thepoints = {
    { x = 0,   y = -10,   z = 2, color = color1 },
    { x = 0,   y = 10000, z = 2, color = color2 },
    { x = 100, y = -2000, z = 2, color = color3 },
}
std3d.reset_canvas(1000)

function love.update()
    width = love.graphics.getWidth()
    height = love.graphics.getHeight()
    std3d.triangle(thepoints)
end

function love.draw()
    std3d.draw_canvas()
    --polygon
    --std2d.line_h({ y = 100, x1 = 100, x2 = 200, color1 = color1, color2 = color3 })
end
