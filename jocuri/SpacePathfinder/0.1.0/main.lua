require("map")
require("class_3D_object")
require("cube")
height = love.graphics.getHeight()
width = love.graphics.getWidth()


--VOXEL CONSTRUCTOR
rotate = { x = 0, y = 0, z = 0 }
voxel = {}
for y = 1, #map do
    voxel[y] = {}
    for x = 1, #map[y] do
        voxel[y][x] = {}

        translate = {}
        translate.y = 0
        translate.x = x - 3
        translate.z = y - 2
        new_object(voxel[y][x], vertices, faces, translate, 0.5, rotate)
    end
end

--GAME UPDATES
function love.update(dt)
    bad = 0
    --Variable constants
    height = love.graphics.getHeight()
    width = love.graphics.getWidth()
    cam_speed = dt * 0.03
    player_speed = dt * 2

    --Camera rotation
    if love.keyboard.isDown("left") then
        for y = 1, #map do
            for x = 1, #map[y] do
                voxel[y][x].rotate.z = voxel[y][x].rotate.z + cam_speed
            end
        end
    end

    if love.keyboard.isDown("right") then
        for y = 1, #map do
            for x = 1, #map[y] do
                voxel[y][x].rotate.z = voxel[y][x].rotate.z - cam_speed
            end
        end
    end

    if love.keyboard.isDown("up") then
        for y = 1, #map do
            for x = 1, #map[y] do
                voxel[y][x].rotate.x = voxel[y][x].rotate.x + cam_speed
            end
        end
    end

    if love.keyboard.isDown("down") then
        for y = 1, #map do
            for x = 1, #map[y] do
                voxel[y][x].rotate.x = voxel[y][x].rotate.x - cam_speed
            end
        end
    end

    --Player movement
    if love.keyboard.isDown("d") then
        for y = 1, #map do
            for x = 1, #map[y] do
                voxel[y][x].translate.x = voxel[y][x].translate.x - math.cos(voxel[y][x].rotate.z) * player_speed
                voxel[y][x].translate.z = voxel[y][x].translate.z - math.sin(voxel[y][x].rotate.z) * player_speed
            end
        end
    end
    if love.keyboard.isDown("a") then
        for y = 1, #map do
            for x = 1, #map[y] do
                voxel[y][x].translate.x = voxel[y][x].translate.x + math.cos(voxel[y][x].rotate.z) * player_speed
                voxel[y][x].translate.z = voxel[y][x].translate.z + math.sin(voxel[y][x].rotate.z) * player_speed
            end
        end
    end
    if love.keyboard.isDown("w") then
        for y = 1, #map do
            for x = 1, #map[y] do
                voxel[y][x].translate.z = voxel[y][x].translate.z - math.cos(-voxel[y][x].rotate.z) * player_speed
                voxel[y][x].translate.x = voxel[y][x].translate.x - math.sin(-voxel[y][x].rotate.z) * player_speed
            end
        end
    end
    if love.keyboard.isDown("s") then
        for y = 1, #map do
            for x = 1, #map[y] do
                voxel[y][x].translate.z = voxel[y][x].translate.z + math.cos(-voxel[y][x].rotate.z) * player_speed
                voxel[y][x].translate.x = voxel[y][x].translate.x + math.sin(-voxel[y][x].rotate.z) * player_speed
            end
        end
    end


    --Hitbox & 'voxel' switching
    xpos = -voxel[1][1].translate.x - 0.5
    zpos = -voxel[1][1].translate.z - 0.5
    x_voxel = -math.floor(voxel[1][1].translate.x - 0.5)
    z_voxel = -math.floor(voxel[1][1].translate.z - 0.5)

    if xpos - x_voxel then
        if map[z_voxel][x_voxel - 1] == 1 then
            bad = 1
        end
    end
    --     if xpos >= 100 then
    --         if map[z_voxel][x_voxel + 1] == 0 then
    --             xpos = 0
    --             x_voxel = x_voxel + 1
    --         else
    --             xpos = 99
    --         end
    --     end
    --     if ypos < 0 then
    --         if map[z_voxel - 1][x_voxel] == 0 then
    --             ypos = 99
    --             z_voxel = z_voxel - 1
    --         else
    --             ypos = 0
    --         end
    --     end
    --     if ypos >= 100 then
    --         if map[z_voxel + 1][x_voxel] == 0 then
    --             ypos = 0
    --             z_voxel = z_voxel + 1
    --         else
    --             ypos = 99
    --         end
    --     end
end

--GAME DRAW
function love.draw()
    for y = 1, #map do
        for x = 1, #map[y] do
            if map[y][x] == 1 then
                voxel[y][x].draw()
                love.graphics.rectangle("line", x * 100 - 100, y * 100 - 100, 99, 99)
            end
        end
    end
    love.graphics.print(x_voxel, 10, 10)
    love.graphics.print(z_voxel, 10, 30)
    love.graphics.print(bad, 10, 50)
    love.graphics.circle("fill", xpos * 100, zpos * 100, 5)
end
