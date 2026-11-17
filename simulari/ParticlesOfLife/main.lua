require("object")
require("physics")

width = love.graphics.getWidth()
height = love.graphics.getWidth()
n = 300
rules = {
    -- { 1,  1,  -1, 1 },  --red
    -- { -1, 2,  2,  -2 }, --yellow
    -- { 1,  -2, 4,  8 },  --blue
    -- { -1, 2,  -8, 16 }, --green

    { 1,   -2,  0,   0.5,  2,   -1 },
    { 8,   10,  0.8, 0.3,  -1,  4 },
    { 0,   0.8, 4,   0.5,  0,   -6 },
    { 0.5, 0.3, 0.5, -0.5, 0,   0 },
    { 8,   -1,  0,   0,    1.5, 14 },
    { 0,   1,   0,   0,    -4,  4 },

    -- { 0, 0, 0, 0, 0, 0 },
    -- { 0, 0, 0, 0, 0, 0 },
    -- { 0, 0, 0, 0, 0, 0 },
    -- { 0, 0, 0, 0, 0, 0 },
    -- { 0, 0, 0, 0, 0, 0 },
    -- { 0, 0, 0, 0, 0, 0 },
}
obj = {}
for i = 1, n / 6 do
    obj[i] = {}
    obj[i] = new_object(1, math.random(2000), math.random(2000), 2000)
    obj[n / 6 + i] = new_object(2, math.random(2000), math.random(2000), 1000)
    obj[n / 3 + i] = new_object(3, math.random(2000), math.random(2000), 800)
    obj[n / 2 + i] = new_object(4, math.random(2000), math.random(2000), 500)
    obj[2 * n / 3 + i] = new_object(5, math.random(2000), math.random(2000), 490)
    obj[5 * n / 6 + i] = new_object(6, math.random(2000), math.random(2000), 50)
end
-- for i = 1, n / 2 do
--     obj[i] = {}
--     obj[i] = new_object(1, math.random(2000), math.random(2000), 2000)
--     obj[n / 2 + i] = new_object(4, math.random(2000), math.random(2000), 100)
-- end

function love.draw()
    for i = 1, n do
        sim_interactions(i, obj, rules)
        border(i, "wall", obj)
        sim_gravity(i, obj)
        if love.keyboard.isDown("w") then
            point_gravity(i, 0, 0, obj, 10)
        end
        obj[i].draw()
    end
end
