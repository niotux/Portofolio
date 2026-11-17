require("objects_def")
require("object")

test = {}
translate = { x = 0, z = 5 }
rotate = { x = 0, z = 0 }
new_object(test, vtx_square, 1, translate, rotate)


function love.update(dt)
    bad = 0
    --Variable constants
    height = love.graphics.getHeight()
    width = love.graphics.getWidth()
    cam_speed = dt * 2
    player_speed = dt * 1.5

    --Camera rotation
    if love.keyboard.isDown("left") then
        test.rotate.z = test.rotate.z + cam_speed
    end

    if love.keyboard.isDown("right") then
        test.rotate.z = test.rotate.z - cam_speed
    end


    --Player movement
    if love.keyboard.isDown("d") then
        test.translate.x = test.translate.x - math.cos(test.rotate.z) * player_speed
        test.translate.z = test.translate.z - math.sin(test.rotate.z) * player_speed
    end
    if love.keyboard.isDown("a") then
        test.translate.x = test.translate.x + math.cos(test.rotate.z) * player_speed
        test.translate.z = test.translate.z + math.sin(test.rotate.z) * player_speed
    end
    if love.keyboard.isDown("w") then
        test.translate.x = test.translate.x - math.sin(-test.rotate.z) * player_speed
        test.translate.z = test.translate.z - math.cos(-test.rotate.z) * player_speed
    end
    if love.keyboard.isDown("s") then
        test.translate.x = test.translate.x + math.sin(-test.rotate.z) * player_speed
        test.translate.z = test.translate.z + math.cos(-test.rotate.z) * player_speed
    end
end

function love.draw()
    test.update()
end
