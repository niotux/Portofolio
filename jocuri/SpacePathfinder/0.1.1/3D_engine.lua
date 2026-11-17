function draw_objects()
    local vtx_z = {}
    local fac_sort = {}
    local fac_order = {}
    count = 0
    for y = 1, #map do
        for x = 1, #map[y] do
            if map[y][x] ~= 0 then
                voxel[y][x].update()
                for i = 1, #voxel[y][x].fac_table do
                    count = count + 1
                    vtx_z[count] =
                        voxel[y][x].vtx_trans[voxel[y][x].fac_table[i][1]].z +
                        voxel[y][x].vtx_trans[voxel[y][x].fac_table[i][2]].z +
                        voxel[y][x].vtx_trans[voxel[y][x].fac_table[i][3]].z

                    fac_order[count] = count
                end
            end
        end
    end
    --Sorting the faces in order

    --Drawing the faces
    -- for i = 1, #self.fac_sort do
    --     if self.vtx_trans[self.fac_sort[i][1]].z >= 0 and self.vtx_trans[self.fac_sort[i][2]].z >= 0 and self.vtx_trans[self.fac_sort[i][3]].z >= 0 then
    --         love.graphics.setColor(1, 1, 1, 1)
    --         love.graphics.polygon("line",
    --             self.vtx_trans[self.fac_sort[i][1]].x * height / 2 / (self.vtx_trans[self.fac_sort[i][1]].z) +
    --             width / 2,
    --             self.vtx_trans[self.fac_sort[i][1]].y * height / 2 / (self.vtx_trans[self.fac_sort[i][1]].z) +
    --             height / 2,
    --             self.vtx_trans[self.fac_sort[i][2]].x * height / 2 / (self.vtx_trans[self.fac_sort[i][2]].z) +
    --             width / 2,
    --             self.vtx_trans[self.fac_sort[i][2]].y * height / 2 / (self.vtx_trans[self.fac_sort[i][2]].z) +
    --             height / 2,
    --             self.vtx_trans[self.fac_sort[i][3]].x * height / 2 / (self.vtx_trans[self.fac_sort[i][3]].z) +
    --             width / 2,
    --             self.vtx_trans[self.fac_sort[i][3]].y * height / 2 / (self.vtx_trans[self.fac_sort[i][3]].z) +
    --             height / 2
    --         )
    --     end
    -- end
end
