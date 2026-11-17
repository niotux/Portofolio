require("std3d")
width = love.graphics.getWidth()
height = love.graphics.getHeight()
std3d.canvas_init(1000)
function love.draw()
    std3d.triangle({
        {
            x = 0,
            y = 0,
            z = 0.5,
            color = { 1, 0, 0 }
        },
        {
            x = 799,
            y = 799,
            z = 2,
            color = { 0, 1, 0 }
        },
        {
            x = 0,
            y = 799,
            z = 2,
            color = { 0, 0, 1 }
        }
    })
    std3d.triangle({
        {
            x = 0,
            y = 0,
            z = 1,
            color = { 1, 0, 0 }
        },
        {
            x = 0,
            y = 499,
            z = 1,
            color = { 0, 1, 0 }
        },
        {
            x = 799,
            y = 799,
            z = 1,
            color = { 0, 0, 1 }
        }
    })
    std3d.canvas_draw()
end
