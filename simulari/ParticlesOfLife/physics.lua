function sim_gravity(i, obj)
    local xa = 0
    local ya = 0
    for j = 1, #obj do
        if i ~= j then
            local dx = obj[j].x - obj[i].x
            local dy = obj[j].y - obj[i].y
            local d = math.sqrt(dx * dx + dy * dy)
            --if d <= 13 then
            --     xa = xa + (13 - d) / 13 * (-dx / d)
            --     ya = ya + (13 - d) / 13 * (-dy / d)
            --else
            xa = xa + dx * obj[j].m / d / 10000
            ya = ya + dy * obj[j].m / d / 10000
            --end
        end
    end
    obj[i].xv = obj[i].xv + xa
    obj[i].yv = obj[i].yv + ya
end

function point_gravity(i, x, y, obj, pow)
    local xa = 0
    local ya = 0
    local dx = x - obj[i].x
    local dy = y - obj[i].y
    local d = math.sqrt(dx * dx + dy * dy)
    xa = xa + 1 / d * dx * pow
    ya = ya + 1 / d * dy * pow

    obj[i].xv = obj[i].xv + xa
    obj[i].yv = obj[i].yv + ya
end

function sim_interactions(i, obj, rules)
    local xa = 0
    local ya = 0
    for j = 1, #obj do
        if i ~= j then
            rule = rules[obj[i].typ][obj[j].typ]
            local dx = obj[j].x - obj[i].x
            local dy = obj[j].y - obj[i].y
            local d = math.sqrt(dx * dx + dy * dy)
            if d <= 33 then
                if d < 10 then
                    friction = 0
                else
                    friction = 0.99 + math.random() * 0.004
                    --friction = 0.999
                    obj[i].xv = obj[i].xv * friction
                    obj[i].yv = obj[i].yv * friction
                    xa = xa + (33 - d) * (-dx / d)
                    ya = ya + (33 - d) * (-dy / d)
                end
            else
                if d <= 233 then
                    xa = xa + (d - 33) * (dx / d) * rule / 200
                    ya = ya + (d - 33) * (dy / d) * rule / 200
                else
                    if d <= 433 then
                        xa = xa + (433 - d) * (dx / d) * rule / 200
                        ya = ya + (433 - d) * (dy / d) * rule / 200
                    end
                end
            end
            --love.graphics.print(d, (obj[i].x + obj[j].x) / 2, (obj[i].y + obj[j].y) / 2)
        end
    end
    obj[i].xv = obj[i].xv + xa
    obj[i].yv = obj[i].yv + ya
end

function border(i, typ, obj)
    if typ == "wall" then
        if obj[i].x > love.graphics.getWidth() * 2 then
            obj[i].x = love.graphics.getWidth() * 2
            obj[i].xv = -obj[i].xv
        end
        if obj[i].x < 0 then
            obj[i].x = 0
            obj[i].xv = -obj[i].xv
        end
        if obj[i].y > love.graphics.getHeight() * 2 then
            obj[i].y = love.graphics.getHeight() * 2
            obj[i].yv = -obj[i].yv
        end
        if obj[i].y < 0 then
            obj[i].y = 0
            obj[i].yv = -obj[i].yv
        end
    end
    if typ == "portal" then
        if obj[i].x > love.graphics.getWidth() * 2 then
            obj[i].x = 0
        end
        if obj[i].x < 0 then
            obj[i].x = love.graphics.getWidth() * 2
        end
        if obj[i].y > love.graphics.getHeight() * 2 then
            obj[i].y = 0
        end
        if obj[i].y < 0 then
            obj[i].y = love.graphics.getHeight() * 2
        end
    end
end
