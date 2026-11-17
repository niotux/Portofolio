require("std3d")
width = love.graphics.getWidth()
height = love.graphics.getHeight()
std3d.canvas_init(1000)
function love.draw()
    std3d.triangle({
        {
            x = 10,
            y = 10,
            z = 1,
            color = { 1, 0, 0 }
        },
        {
            x = 0,
            y = 0,
            z = 1,
            color = { 1, 0, 1 }
        },
        {
            x = 999,
            y = 0,
            z = 1,
            color = { 1, 0, 1 }
        }
    })
    std3d.canvas_draw()
end
