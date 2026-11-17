love.graphics.setDefaultFilter("nearest", "nearest")
--INCLUDE FILES
require("background")
require("textures_load")

require("main_menu")
require("game")

--INCLUDE CLASSES
require("class_system")
require("class_ship")
require("class_button")

--GLOBAL VARIABLES
mapKey = false
dt = 1
xmap = 10
ymap = 10
cnvs = love.graphics.newCanvas(minRES, minRES)
stage = "menu"


MENU_init()

GAME_init()
--ACTIVE PART--------------------------------------------------------------------------------------

--CONTROL AND UPDATE
function love.update(dt)
    WinW = love.graphics.getWidth()
    WinH = love.graphics.getHeight()

    minRES = math.min(WinW, WinH)
    maxRES = math.max(WinW, WinH)

    unit = minRES / 1000        --unit = 1000th of the resolution
    wCrct = (WinW - minRES) / 2 --corrects for canvas translation
    hCrct = (WinH - minRES) / 2 --corrects for canvas translation


    if stage == "game" then
        GAME_update()
    end
end

--mousepressed
function love.mousepressed(xx, yy, mouse_butt)
    if stage == "menu" then
        butt_quit.mousepressed(mouse_butt)
        butt_newgame.mousepressed(mouse_butt)
        butt_settings.mousepressed(mouse_butt)
        butt_loadgame.mousepressed(mouse_butt)
    end
end

--mousereleased
function love.mousereleased(xx, yy, mouse_butt)
    if stage == "menu" then
        butt_quit.mousereleased(mouse_butt)
        butt_newgame.mousereleased(mouse_butt)
        butt_settings.mousereleased(mouse_butt)
        butt_loadgame.mousereleased(mouse_butt)
    end
end

--keypressed
function love.keypressed(key)
    if stage == "game" and key == "m" then
        if (mapKey == false) then
            mapKey = true

            mapdisplay()
        else
            mapKey = false
            stars(200)
        end
    end
end

--DRAW
function love.draw()
    if stage == "menu" then
        MENU()
    else
        GAME()
    end




    -- love.graphics.print(player_ship.xsec, 0, 0)
    -- love.graphics.print(player_ship.ysec, 0, 10)
    -- love.graphics.print(tostring(butt_newgame.pressed), 0, 110)
    -- love.graphics.print(tostring(butt_loadgame.pressed), 0, 120)
    -- love.graphics.print(tostring(butt_settings.pressed), 0, 130)
    -- love.graphics.print(tostring(butt_quit.pressed), 0, 140)
end
