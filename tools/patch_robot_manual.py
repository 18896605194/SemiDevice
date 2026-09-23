from pathlib import Path

p = Path(r"D:\Code\xyz.Core\Client\xyz.Client.Manual\Views\RobotManualControl.xaml")
t = p.read_text(encoding="utf-8")

idx = t.find('MinHeight="240"')
i1 = t.find("<ItemsControl", idx)
south = t.find("SouthStations")
i2 = t.find("</ItemsControl>", south)
i2 = t.find(">", i2) + 1

replacement = """<!--  北：腔体用 ChamberInfoCard  -->
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
                            </ItemsControl>"""

t = t[:i1] + replacement + t[i2:]
p.write_text(t, encoding="utf-8")
print("ok", len(t))
