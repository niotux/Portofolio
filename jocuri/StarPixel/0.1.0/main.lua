Hres = 1000
Vres = 1000
--ship space coords
x = Hres / 2
y = Vres / 2
--ship props
speed = 0.02
--ship sector coords
xsec = 5
ysec = 5
xqvd = 0
yqvd = 0
--counts & other vars.
xmap = 10
ymap = 10
mapKey = false
dt = 1
--requires
require("MapTables")
require("MapDisplay")
--loading images
function love.load()
    backgroundIMG = love.graphics.newImage("textures/galaxy.png")
    backgroundIMG:setFilter("nearest", "nearest")
    shipIMG = love.graphics.newImage("textures/ship00.png")
    shipIMG:setFilter("nearest", "nearest")
    systemIMG = love.graphics.newImage("textures/1faction.png")
    systemIMG:setFilter("nearest", "nearest")
end

--canvas functions
function stars(Nbr)
    cnvs = love.graphics.newCanvas(Hres, Vres)
    love.graphics.setCanvas(cnvs)
    love.math.setRandomSeed(xsec + ysec * 10)
    for i = 0, Nbr, 1 do
        love.graphics.points(love.math.random(0, Hres), love.math.random(0, Vres))
    end
    love.graphics.setCanvas()
end

--function for loading sector on screen
function secinit(x, y)
    --Objects
end

--function for map bounds / hitboxes
function border(NrSec)
    if x < 0 and xsec < 0 then
        x = 0
        xsec = 0
    end
    if y < 0 and ysec < 0 then
        y = 0
        ysec = 0
    end
    if x > Hres and xsec > NrSec then
        x = Hres - 1
        xsec = NrSec - 1
    end
    if y > Vres and ysec > NrSec then
        y = Vres - 1
        ysec = NrSec - 1
    end
end

--functions callouts
stars(200)
for iy = 0, 9, 1 do
    for ix = 0, 9, 1 do
        secinit(ix, iy)
    end
end

--ACTIVE PART
function love.update(dt)
    function love.draw()
        --KEYBINDS--

        --ship movement
        if (love.keyboard.isDown("w") == true) then
            y = y - (speed * Vres)
        end
        if (love.keyboard.isDown("s") == true) then
            y = y + (speed * Vres)
        end
        if (love.keyboard.isDown("a") == true) then
            x = x - (speed * Hres)
        end
        if (love.keyboard.isDown("d") == true) then
            x = x + (speed * Hres)
        end

        --activate map
        function love.keypressed(key)
            if key == "m" then
                if (mapKey == false) then
                    mapKey = true

                    mapdisplay("galaxy", xqvd, yqvd)
                else
                    mapKey = false
                    stars(200)
                end
            end
        end

        --CAUSALITIES

        --sector switching
        if (x > Hres) then
            xsec = xsec + 1
            x = 0
            if (mapKey == false) then
                stars(200)
            end
        end
        if (x < 0) then
            xsec = xsec - 1
            x = Hres - 1
            if (mapKey == false) then
                stars(200)
            end
        end
        if (y > Vres) then
            ysec = ysec + 1
            y = 0
            if (mapKey == false) then
                stars(200)
            end
        end
        if (y < 0) then
            ysec = ysec - 1
            y = Vres - 1
            if (mapKey == false) then
                stars(200)
            end
        end
        --qvadrant switching
        if (xsec > xmap - 1) then
            xsec = 0
            xqvd = xqvd + 1
            if (mapKey == true) then
                mapdisplay("galaxy", xqvd, yqvd)
            end
        end
        if (xsec < 0) then
            xsec = xmap - 1
            xqvd = xqvd - 1
            if (mapKey == true) then
                mapdisplay("galaxy", xqvd, yqvd)
            end
        end
        if (ysec > ymap - 1) then
            ysec = 0
            yqvd = yqvd + 1
            if (mapKey == true) then
                mapdisplay("galaxy", xqvd, yqvd)
            end
        end
        if (ysec < 0) then
            ysec = ymap - 1
            yqvd = yqvd - 1
            if (mapKey == true) then
                mapdisplay("galaxy", xqvd, yqvd)
            end
        end
        --border("<", xsec, 0)

        --GRAPHICS

        --draws canvas
        love.graphics.draw(cnvs)

        --drawing the ship
        if (mapKey == true) then
            love.graphics.setColor(0, 0, 0.5)
            love.graphics.rectangle("fill", xsec * (Hres / xmap), ysec * (Vres / ymap), (Hres / xmap) - 1,
                (Vres / ymap) - 1)
            love.graphics.setColor(1, 1, 0)
            imageH = shipIMG:getWidth()
            imageV = shipIMG:getHeight()
            love.graphics.draw(shipIMG, x / xmap + xsec * (Hres / xmap) - imageH / 4,
                y / ymap + ysec * (Vres / ymap) - imageV / 2, 0, 1, 1)
            love.graphics.setColor(1, 1, 1)
        else
            --love.graphics.circle("fill", x, y, 30)
            imageH = shipIMG:getWidth()
            imageV = shipIMG:getHeight()
            love.graphics.draw(shipIMG, x - imageH * 2, y - imageV * 2, 0, 4, 4)
        end
        love.graphics.print(xsec, 0, 0)
        love.graphics.print(ysec, 0, 10)
    end
end
