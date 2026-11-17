function GAME_init()
    player_ship = {}
    new_ship(player_ship, 500, 500, 5, 5, "player", 1, false)
end

function GAME_update()
    player_ship.update()
end

function GAME()
    if (mapKey == false) then
        stars(200)
    end
    if (mapKey == true) then
        mapdisplay()
    end
    love.graphics.draw(cnvs, wCrct, hCrct)
    player_ship.draw()
end
