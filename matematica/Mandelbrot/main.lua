require("fractal")
z = 300
x_pos = -0.48
y_pos = 0
precision = 0
fractal(z, x_pos, y_pos, precision)

function love.update(dt)
    if love.keyboard.isDown("up") then
        precision = precision + 10
        fractal(z, x_pos, y_pos, precision)
    end
    if love.keyboard.isDown("down") then
        precision = precision - 10
        fractal(z, x_pos, y_pos, precision)
    end
    if love.keyboard.isDown("e") then
        z = z * 2
        fractal(z, x_pos, y_pos, precision)
    end
    if love.keyboard.isDown("q") then
        z = z / 2
        fractal(z, x_pos, y_pos, precision)
    end
    if love.keyboard.isDown("d") then
        x_pos = x_pos + 60 / z
        fractal(z, x_pos, y_pos, precision)
    end
    if love.keyboard.isDown("a") then
        x_pos = x_pos - 60 / z
        fractal(z, x_pos, y_pos, precision)
    end
    if love.keyboard.isDown("w") then
        y_pos = y_pos - 60 / z
        fractal(z, x_pos, y_pos, precision)
    end
    if love.keyboard.isDown("s") then
        y_pos = y_pos + 60 / z
        fractal(z, x_pos, y_pos, precision)
    end
end

function love.draw()
    love.graphics.draw(cnvs, 0, 0)
end
