require("map")
require("class_3D_object")
require("3D_engine")
require("cube")
height = love.graphics.getHeight()
width = love.graphics.getWidth()


--VOXEL CONSTRUCTOR-----------------------------------------------------------------------------------------
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

x_voxel = -math.floor(voxel[1][1].translate.x - 0.5)
z_voxel = -math.floor(voxel[1][1].translate.z - 0.5)

--GAME UPDATES-----------------------------------------------------------------------------------------
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


    --Hitbox
    xpos = -voxel[1][1].translate.x + 1.5
    zpos = -voxel[1][1].translate.z + 1.5

    if xpos < x_voxel + 0.1 then
        if map[z_voxel][x_voxel - 1] == 1 then
            for y = 1, #map do
                for x = 1, #map[y] do
                    voxel[y][x].translate.x = voxel[y][x].translate.x - (x_voxel - xpos + 0.1)
                end
            end
        end
    end
    if xpos > x_voxel + 0.9 then
        if map[z_voxel][x_voxel + 1] == 1 then
            for y = 1, #map do
                for x = 1, #map[y] do
                    voxel[y][x].translate.x = voxel[y][x].translate.x + (xpos - x_voxel - 0.9)
                end
            end
        end
    end
    if zpos < z_voxel + 0.1 then
        if map[z_voxel - 1][x_voxel] == 1 then
            for y = 1, #map do
                for x = 1, #map[y] do
                    voxel[y][x].translate.z = voxel[y][x].translate.z - (z_voxel - zpos + 0.1)
                end
            end
        end
    end
    if zpos > z_voxel + 0.9 then
        if map[z_voxel + 1][x_voxel] == 1 then
            for y = 1, #map do
                for x = 1, #map[y] do
                    voxel[y][x].translate.z = voxel[y][x].translate.z + (zpos - z_voxel - 0.9)
                end
            end
        end
    end
    x_voxel = -math.floor(voxel[1][1].translate.x - 0.5)
    z_voxel = -math.floor(voxel[1][1].translate.z - 0.5)
end

--GAME DRAW-----------------------------------------------------------------------------------------
function love.draw()
    for y = 1, #map do
        for x = 1, #map[y] do
            if map[y][x] == 1 then
                voxel[y][x].update()
                -- love.graphics.rectangle("line", x * 100 - 100, y * 100 - 100, 99, 99)
            end
        end
    end
    draw_objects()
    -- love.graphics.print(x_voxel, 10, 10)
    -- love.graphics.print(z_voxel, 80, 10)
    -- love.graphics.print(xpos, 10, 30)
    -- love.graphics.print(zpos, 80, 30)
    -- love.graphics.print(bad, 10, 50)
    -- love.graphics.circle("fill", xpos * 100 - 100, zpos * 100 - 100, 5)
end
