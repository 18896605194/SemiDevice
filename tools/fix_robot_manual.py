from pathlib import Path

p = Path(r"D:\Code\xyz.Core\Client\xyz.Client.Manual\Views\RobotManualControl.xaml")
t = p.read_text(encoding="utf-8")

marker = 'MinHeight="240"'
idx = t.find(marker)
if idx < 0:
    raise SystemExit("marker not found")

grid_start = t.find("<Grid", idx)
ops = t.find("robotmanual.params", idx)

pos = idx
ops_col = -1
while True:
    pos = t.find('Grid.Column="1"', pos + 1)
    if pos < 0 or (ops > 0 and pos > ops + 5000):
        break
    if ops > 0 and pos < ops:
        ops_col = pos

if ops_col < 0:
    ops_col = t.find('Grid.Column="1"', t.find("LoadPortInfoCard", idx))

i1 = t.find("<ItemsControl", grid_start)
tag_start = t.rfind("<Grid", 0, ops_col)

replacement = """<!--  北：腔体 ChamberInfoCard  -->
                            <ItemsControl
                                Grid.Row="0"
                                Grid.Column="1"
                                Margin="0,0,0,8"
                                ItemsSource="{Binding Model.NorthStations}">
                                <ItemsControl.ItemsPanel>
                                    <ItemsPanelTemplate>
                                        <StackPanel Orientation="Horizontal" />
                                    </ItemsPanelTemplate>
                                </ItemsControl.ItemsPanel>
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <presentationControls:ChamberInfoCard
                                            Width="220"
                                            Height="160"
                                            Margin="4,0"
                                            CreateEnable="False"
                                            DeleteEnable="False"
                                            IsOnline=""
                                            Recipe=""
                                            RecipeState=""
                                            RotationSpeed="0"
                                            ShutterState=""
                                            State=""
                                            StatusOn="False"
                                            Step=""
                                            StepTime=""
                                            RecipeTime=""
                                            Title="{Binding Title}" />
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>

                            <Viewbox
                                Grid.Row="1"
                                Grid.Column="1"
                                MaxWidth="480"
                                MaxHeight="480"
                                Stretch="Uniform">
                                <presentationControls:Robot
                                    ArmCount="{Binding Model.ArmCount}"
                                    ArmType="Linear"
                                    Arms="{Binding Model.Arms}"
                                    Rotation="{Binding Model.Direction}"
                                    Travel="{Binding Model.Y}" />
                            </Viewbox>

                            <!--  南：LoadPort 用 LoadPortInfoCard  -->
                            <ItemsControl
                                Grid.Row="2"
                                Grid.Column="1"
                                Margin="0,8,0,0"
                                ItemsSource="{Binding Model.SouthStations}">
                                <ItemsControl.ItemsPanel>
                                    <ItemsPanelTemplate>
                                        <StackPanel Orientation="Horizontal" />
                                    </ItemsPanelTemplate>
                                </ItemsControl.ItemsPanel>
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <presentationControls:LoadPortInfoCard
                                            Width="280"
                                            Height="180"
                                            Margin="4,0"
                                            Alarm="False"
                                            AutoMode="True"
                                            CreateEnable="False"
                                            DeleteEnable="False"
                                            IsConnected="False"
                                            IsOnline=""
                                            Placed="False"
                                            Present="False"
                                            RotationSpeed="0"
                                            SlotCount="0"
                                            Title="{Binding Title}" />
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </Grid>
                    </Grid>

                    """

new_t = t[:i1] + replacement + t[tag_start:]
p.write_text(new_t, encoding="utf-8")
print("repaired", len(new_t), "Chamber", new_t.count("ChamberInfoCard"), "LP", new_t.count("LoadPortInfoCard"))
